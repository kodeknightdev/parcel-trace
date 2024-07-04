using System.Net;
using System.Net.Http.Json;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;
using ParcelTrace.Protos.Shipments;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Endpoints;
using ParcelTrace.Shipments.Messaging;
using ParcelTrace.Shipments.Outbox;
using ParcelTrace.TestSupport;

namespace ParcelTrace.Shipments.IntegrationTests;

public class ShipmentsApiTests(ShipmentsApiFactory factory) : IClassFixture<ShipmentsApiFactory>
{
    private static readonly AddressDto Alexanderplatz = new("Alexanderplatz 1", "Berlin", "10178", "DE", 52.5219, 13.4132);
    private static readonly AddressDto Kudamm = new("Kurfürstendamm 21", "Berlin", "10719", "DE", 52.5030, 13.3320);

    private static CreateShipmentRequest NewRequest(decimal weightKg = 1.2m, ServiceLevel level = ServiceLevel.Standard) => new(
        new PartyDto("Alice Moreau", "+49 30 1234567"),
        new PartyDto("Jonas Weber", "+49 151 2345678"),
        Alexanderplatz, Kudamm, new PackageDto(weightKg, 30, 20, 10), level, "Fragile");

    private async Task<ShipmentDto> CreateAsync(HttpClient client, CreateShipmentRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/shipments", request ?? NewRequest(), JsonDefaults.Options);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShipmentDto>(JsonDefaults.Options))!;
    }

    [DockerFact]
    public async Task Creating_a_shipment_stores_it_and_stages_shipments_created_in_the_outbox()
    {
        var customer = Guid.NewGuid();
        var shipment = await CreateAsync(factory.ClientFor(customer, Roles.Customer));

        shipment.TrackingNumber.Should().MatchRegex(@"^PT-\d{4}-[A-Z2-9]{6}$");
        shipment.Status.Should().Be(ShipmentStatus.Created);
        shipment.Price.Should().BeGreaterThan(0);
        shipment.EtaAt.Should().NotBeNull("the initial ETA comes from the eta service");
        shipment.CanCancel.Should().BeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        var outbox = await db.OutboxMessages.Where(m => m.Key == shipment.Id.ToString()).ToListAsync();
        outbox.Should().ContainSingle(m => m.Topic == Topics.ShipmentCreated && m.PublishedAt == null);
        outbox[0].Payload.Should().Contain(shipment.TrackingNumber).And.Contain("\"eventType\":\"ShipmentCreated\"");
    }

    [DockerFact]
    public async Task Creating_still_works_when_the_eta_service_is_down()
    {
        factory.Eta.Next = null;
        try
        {
            var shipment = await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));
            shipment.EtaAt.Should().BeNull();
        }
        finally
        {
            factory.Eta.Next = new(7.4, 31, DateTimeOffset.UtcNow.AddMinutes(31), 1.15);
        }
    }

    [DockerFact]
    public async Task Invalid_parcels_get_validation_problem_details()
    {
        var response = await factory.ClientFor(Guid.NewGuid(), Roles.Customer)
            .PostAsJsonAsync("/api/shipments", NewRequest(weightKg: 75), JsonDefaults.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("WeightKg");
    }

    [DockerFact]
    public async Task Customers_only_see_their_own_shipments()
    {
        var alice = factory.ClientFor(Guid.NewGuid(), Roles.Customer);
        var bob = factory.ClientFor(Guid.NewGuid(), Roles.Customer);
        var shipment = await CreateAsync(alice);

        var mine = await alice.GetFromJsonAsync<List<ShipmentDto>>("/api/shipments/mine", JsonDefaults.Options);
        mine.Should().ContainSingle(s => s.Id == shipment.Id);

        (await bob.GetAsync($"/api/shipments/{shipment.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<List<ShipmentDto>>("/api/shipments/mine", JsonDefaults.Options)).Should().BeEmpty();
    }

    [DockerFact]
    public async Task Cancelling_inside_the_window_stages_shipments_cancelled()
    {
        var client = factory.ClientFor(Guid.NewGuid(), Roles.Customer);
        var shipment = await CreateAsync(client);

        var response = await client.PostAsJsonAsync($"/api/shipments/{shipment.Id}/cancel", new { reason = "Booked twice" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ShipmentDto>(JsonDefaults.Options))!.Status.Should().Be(ShipmentStatus.Cancelled);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        (await db.OutboxMessages.AnyAsync(m => m.Key == shipment.Id.ToString() && m.Topic == Topics.ShipmentCancelled)).Should().BeTrue();
    }

    [DockerFact]
    public async Task Public_tracking_hides_personal_details()
    {
        var shipment = await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));

        var anonymous = factory.CreateClient();
        var body = await anonymous.GetStringAsync($"/api/shipments/track/{shipment.TrackingNumber.ToLowerInvariant()}");

        body.Should().Contain(shipment.TrackingNumber).And.Contain("Berlin");
        body.Should().NotContain("Jonas").And.NotContain("Alexanderplatz").And.NotContain("+49");
        (await anonymous.GetAsync("/api/shipments/track/nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [DockerFact]
    public async Task Events_from_dispatch_and_tracking_move_the_shipment_and_are_applied_once()
    {
        var shipment = await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));
        var courierId = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var delivered = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        async Task Handle(Func<ShipmentEventHandler, Task> action)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<ShipmentEventHandler>());
        }

        await Handle(h => h.HandleCourierAssignedAsync(assigned, new CourierAssigned(shipment.Id, shipment.TrackingNumber, courierId, "Lena Fischer", AssignmentMode.Auto, now), default));
        await Handle(h => h.HandleCourierAssignedAsync(assigned, new CourierAssigned(shipment.Id, shipment.TrackingNumber, courierId, "Lena Fischer", AssignmentMode.Auto, now), default));
        await Handle(h => h.HandleStatusChangedAsync(Guid.NewGuid(), new StatusChanged(shipment.Id, shipment.TrackingNumber, courierId, ShipmentStatus.PickedUp, null, null, now.AddMinutes(10)), default));
        await Handle(h => h.HandleStatusChangedAsync(Guid.NewGuid(), new StatusChanged(shipment.Id, shipment.TrackingNumber, courierId, ShipmentStatus.OutForDelivery, null, null, now.AddMinutes(20)), default));
        var pod = new ProofOfDelivery("Jonas Weber", "J. Weber", null, now.AddMinutes(30));
        await Handle(h => h.HandleStatusChangedAsync(delivered, new StatusChanged(shipment.Id, shipment.TrackingNumber, courierId, ShipmentStatus.Delivered, null, pod, now.AddMinutes(30)), default));
        await Handle(h => h.HandleStatusChangedAsync(delivered, new StatusChanged(shipment.Id, shipment.TrackingNumber, courierId, ShipmentStatus.Delivered, null, pod, now.AddMinutes(30)), default));

        var courier = factory.ClientFor(Guid.NewGuid(), Roles.Courier, courierId: courierId);
        var result = await courier.GetFromJsonAsync<ShipmentDto>($"/api/shipments/{shipment.Id}", JsonDefaults.Options);
        result!.Status.Should().Be(ShipmentStatus.Delivered);
        result.CourierName.Should().Be("Lena Fischer");
        result.Proof!.RecipientName.Should().Be("Jonas Weber");
        result.EtaAt.Should().BeNull();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        (await db.ProcessedMessages.CountAsync(m => m.EventId == assigned || m.EventId == delivered)).Should().Be(2);
    }

    [DockerFact]
    public async Task Out_of_order_status_throws_so_the_consumer_retries_it()
    {
        var shipment = await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ShipmentEventHandler>();

        var act = () => handler.HandleStatusChangedAsync(Guid.NewGuid(),
            new StatusChanged(shipment.Id, shipment.TrackingNumber, Guid.NewGuid(), ShipmentStatus.PickedUp, null, null, DateTimeOffset.UtcNow), default);

        await act.Should().ThrowAsync<Exception>();
    }

    [DockerFact]
    public async Task Outbox_relay_publishes_pending_messages_in_order_and_marks_them()
    {
        var client = factory.ClientFor(Guid.NewGuid(), Roles.Customer);
        var first = await CreateAsync(client);
        var second = await CreateAsync(client);
        var publisher = new RecordingPublisher();
        var relay = new OutboxPublisher(factory.Services.GetRequiredService<IServiceScopeFactory>(), publisher,
            Options.Create(new OutboxOptions { BatchSize = 500 }), Options.Create(new KafkaOptions { Enabled = true }),
            TimeProvider.System, NullLogger<OutboxPublisher>.Instance);

        while (await relay.PublishBatchAsync(default) > 0)
        {
        }

        var keys = publisher.Published.Select(p => p.Key).ToList();
        keys.Should().ContainInOrder(first.Id.ToString(), second.Id.ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        (await db.OutboxMessages.AnyAsync(m => m.PublishedAt == null)).Should().BeFalse();
    }

    [DockerFact]
    public async Task Dispatch_can_look_up_a_shipment_over_grpc()
    {
        var shipment = await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));
        using var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var client = new ShipmentLookup.ShipmentLookupClient(channel);

        var details = await client.GetShipmentAsync(new GetShipmentRequest { ShipmentId = shipment.Id.ToString() });

        details.TrackingNumber.Should().Be(shipment.TrackingNumber);
        details.Status.Should().Be("Created");
        details.Pickup.Lat.Should().BeApproximately(52.5219, 0.0001);
        details.WeightKg.Should().Be(1.2);
    }

    [DockerFact]
    public async Task Dashboard_is_for_dispatchers()
    {
        await CreateAsync(factory.ClientFor(Guid.NewGuid(), Roles.Customer));

        var dispatcher = factory.ClientFor(Guid.NewGuid(), Roles.Dispatcher);
        var counts = await dispatcher.GetFromJsonAsync<DailyCounts>("/api/shipments/dashboard/daily", JsonDefaults.Options);
        counts!.Created.Should().BeGreaterThan(0);
        counts.Unassigned.Should().BeGreaterThan(0);

        (await factory.ClientFor(Guid.NewGuid(), Roles.Customer).GetAsync("/api/shipments/dashboard/daily")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/shipments/dashboard/daily")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<(string Topic, string Key)> Published { get; } = [];

        public Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default) =>
            PublishRawAsync(topic, key, eventType, "{}", null, ct);

        public Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? traceParent, CancellationToken ct = default)
        {
            Published.Add((topic, key));
            return Task.CompletedTask;
        }
    }
}
