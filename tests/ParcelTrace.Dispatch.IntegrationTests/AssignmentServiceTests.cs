using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MongoDB.Driver;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Domain;
using ParcelTrace.Dispatch.Services;
using ParcelTrace.Dispatch.Shipments;
using ParcelTrace.TestSupport;

namespace ParcelTrace.Dispatch.IntegrationTests;

public class AssignmentServiceTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
    private static readonly ShipmentLocation Alexanderplatz = new("Alexanderplatz 1", "Berlin", "10178", 52.5219, 13.4132);
    private static readonly ShipmentLocation Kudamm = new("Kurfürstendamm 21", "Berlin", "10719", 52.5030, 13.3320);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2024, 3, 20, 9, 30, 0, TimeSpan.Zero));
    private readonly FakeDirectory _directory = new();
    private readonly RecordingPublisher _publisher = new();

    private AssignmentService Service(DispatchDb db) =>
        new(db, _directory, _publisher, Options.Create(new DispatchOptions { OfferTimeoutSeconds = 120, MaxOfferAttempts = 3 }),
            _clock, NullLogger<AssignmentService>.Instance);

    private ShipmentInfo AddShipment(double weightKg = 2, ShipmentStatus status = ShipmentStatus.Created)
    {
        var id = Guid.NewGuid();
        var info = new ShipmentInfo(id, $"PT-2024-{id.ToString("N")[..6].ToUpperInvariant()}", status, ServiceLevel.Standard, weightKg,
            Alexanderplatz, Kudamm, "Jonas Weber", null, _clock.GetUtcNow().AddHours(48).UtcDateTime);
        _directory.Shipments[id] = info;
        return info;
    }

    private ShipmentCreated Created(ShipmentInfo s) => new(s.ShipmentId, s.TrackingNumber, Guid.NewGuid(), s.ServiceLevel,
        new AddressSummary(s.Pickup.City, s.Pickup.PostalCode, s.Pickup.Lat, s.Pickup.Lng),
        new AddressSummary(s.Dropoff.City, s.Dropoff.PostalCode, s.Dropoff.Lat, s.Dropoff.Lng),
        (decimal)s.WeightKg, s.SlaDueAt, null, _clock.GetUtcNow());

    private static Courier Courier(string name, double lat, double lng, bool available = true, int max = 5, double capacity = 100) => new()
    {
        Name = name,
        VehicleType = VehicleType.Car,
        CapacityKg = capacity,
        MaxActiveShipments = max,
        IsAvailable = available,
        HomeBase = Position.From(lat, lng)
    };

    private static Task<Courier> Reload(DispatchDb db, Guid id) => db.Couriers.Find(c => c.Id == id).FirstAsync();

    private async Task<(DispatchDb Db, AssignmentService Service, ShipmentInfo Shipment)> QueuedAsync(params Courier[] couriers)
    {
        var db = await mongo.NewDatabaseAsync();
        if (couriers.Length > 0)
        {
            await db.Couriers.InsertManyAsync(couriers);
        }

        var shipment = AddShipment(weightKg: 3);
        var service = Service(db);
        await service.QueueAsync(Created(shipment), default);
        return (db, service, shipment);
    }

    [DockerFact]
    public async Task The_nearest_courier_gets_an_offer_and_their_capacity_is_held()
    {
        var near = Courier("Near", 52.5200, 13.4100);
        var far = Courier("Far", 52.4500, 13.3000);
        var (db, service, shipment) = await QueuedAsync(near, far);

        var result = await service.OfferAsync(shipment.ShipmentId, default);

        result.Offered.Should().BeTrue();
        result.CourierId.Should().Be(near.Id);
        result.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddMinutes(2));
        (await Reload(db, near.Id)).CurrentLoadKg.Should().Be(3, "the offer holds capacity until it is answered");
        _publisher.Published.Should().BeEmpty("nothing is assigned until the courier accepts");
        (await service.OffersForCourierAsync(near.Id, default)).Should().ContainSingle();
    }

    [DockerFact]
    public async Task Accepting_creates_the_assignment_and_publishes_courier_assigned()
    {
        var near = Courier("Near", 52.5200, 13.4100);
        var (db, service, shipment) = await QueuedAsync(near);
        await service.OfferAsync(shipment.ShipmentId, default);

        var assignment = await service.AcceptOfferAsync(shipment.ShipmentId, near.Id, default);

        assignment.Mode.Should().Be(AssignmentMode.Auto);
        (await Reload(db, near.Id)).ActiveCount.Should().Be(1, "the hold becomes the assignment, not a second reservation");
        (await db.Pending.CountDocumentsAsync(FilterDefinition<PendingShipment>.Empty)).Should().Be(0);
        (await service.LoadSagaAsync(shipment.ShipmentId, default))!.State.Should().Be(SagaState.Accepted);
        _publisher.Published.Should().ContainSingle(p => p.Topic == Topics.CourierAssigned && p.Key == shipment.ShipmentId.ToString());
    }

    [DockerFact]
    public async Task A_rejection_releases_the_hold_and_offers_the_next_courier()
    {
        var near = Courier("Near", 52.5200, 13.4100);
        var second = Courier("Second", 52.5100, 13.4000);
        var (db, service, shipment) = await QueuedAsync(near, second);
        await service.OfferAsync(shipment.ShipmentId, default);

        var next = await service.RejectOfferAsync(shipment.ShipmentId, near.Id, "Too far", default);

        next.CourierId.Should().Be(second.Id);
        (await Reload(db, near.Id)).ActiveCount.Should().Be(0);
        (await Reload(db, second.Id)).ActiveCount.Should().Be(1);
        (await db.Pending.Find(p => p.ShipmentId == shipment.ShipmentId).FirstAsync()).OfferedTo.Should().Be("Second");
    }

    [DockerFact]
    public async Task Unanswered_offers_time_out_and_three_failures_escalate()
    {
        var couriers = new[] { Courier("A", 52.5210, 13.4120), Courier("B", 52.5150, 13.4050), Courier("C", 52.5100, 13.4000), Courier("D", 52.5000, 13.3900) };
        var (db, service, shipment) = await QueuedAsync(couriers);
        await service.OfferAsync(shipment.ShipmentId, default);

        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));
            (await service.ExpireOffersAsync(default)).Should().Be(1);
        }

        var saga = await service.LoadSagaAsync(shipment.ShipmentId, default);
        saga!.State.Should().Be(SagaState.Escalated);
        saga.Offers.Select(o => o.Outcome).Should().AllBeEquivalentTo(OfferOutcome.TimedOut);
        saga.Offers.Select(o => o.CourierName).Should().Equal("A", "B", "C");
        (await db.Pending.Find(p => p.ShipmentId == shipment.ShipmentId).FirstAsync()).Escalated.Should().BeTrue();
        foreach (var c in couriers)
        {
            (await Reload(db, c.Id)).ActiveCount.Should().Be(0, "every hold was released");
        }

        (await service.OfferPendingAsync(default)).Should().BeEmpty("escalated shipments wait for a dispatcher");
    }

    [DockerFact]
    public async Task A_dispatcher_resolves_an_escalation_by_assigning_directly()
    {
        var a = Courier("A", 52.5210, 13.4120);
        var (db, service, shipment) = await QueuedAsync(a);
        await service.OfferAsync(shipment.ShipmentId, default);

        var assignment = await service.AssignManualAsync(shipment.ShipmentId, a.Id, "dispatcher@parceltrace.local", default);

        assignment.Mode.Should().Be(AssignmentMode.Manual);
        (await Reload(db, a.Id)).ActiveCount.Should().Be(1, "the offer hold was withdrawn before the assignment reserved again");
        (await service.LoadSagaAsync(shipment.ShipmentId, default))!.State.Should().Be(SagaState.ManuallyAssigned);
    }

    [DockerFact]
    public async Task Without_a_free_courier_the_shipment_keeps_waiting()
    {
        var (db, service, shipment) = await QueuedAsync(Courier("Off", 52.52, 13.41, available: false));

        var result = await service.OfferAsync(shipment.ShipmentId, default);

        result.Offered.Should().BeFalse();
        result.State.Should().Be(SagaState.WaitingForCourier);
        (await db.Pending.Find(p => p.ShipmentId == shipment.ShipmentId).FirstAsync()).Attempts.Should().Be(1);
    }

    [DockerFact]
    public async Task Cancelling_compensates_an_open_offer_and_an_active_assignment()
    {
        var max = Courier("Max", 52.52, 13.41);
        var lena = Courier("Lena", 52.50, 13.40);
        var (db, service, offered) = await QueuedAsync(max);
        await db.Couriers.InsertOneAsync(lena);
        await service.OfferAsync(offered.ShipmentId, default);
        var assigned = AddShipment();
        await service.AssignManualAsync(assigned.ShipmentId, lena.Id, "dispatcher", default);

        await service.CancelAsync(new ShipmentCancelled(offered.ShipmentId, offered.TrackingNumber, null, "x", _clock.GetUtcNow()), default);
        await service.CancelAsync(new ShipmentCancelled(assigned.ShipmentId, assigned.TrackingNumber, lena.Id, "x", _clock.GetUtcNow()), default);

        (await Reload(db, max.Id)).ActiveCount.Should().Be(0);
        (await Reload(db, lena.Id)).ActiveCount.Should().Be(0);
        (await service.LoadSagaAsync(offered.ShipmentId, default))!.State.Should().Be(SagaState.Cancelled);
        (await db.Pending.CountDocumentsAsync(FilterDefinition<PendingShipment>.Empty)).Should().Be(0);
    }

    [DockerFact]
    public async Task Delivery_frees_the_courier_exactly_once()
    {
        var db = await mongo.NewDatabaseAsync();
        var courier = Courier("Lena", 52.52, 13.41);
        await db.Couriers.InsertOneAsync(courier);
        var shipment = AddShipment(weightKg: 4);
        var service = Service(db);
        await service.AssignManualAsync(shipment.ShipmentId, courier.Id, "dispatch", default);

        var delivered = new StatusChanged(shipment.ShipmentId, shipment.TrackingNumber, courier.Id, ShipmentStatus.Delivered, null, null, _clock.GetUtcNow());
        await service.CompleteAsync(delivered, default);
        await service.CompleteAsync(delivered, default);

        var reloaded = await Reload(db, courier.Id);
        reloaded.ActiveCount.Should().Be(0);
        reloaded.CurrentLoadKg.Should().Be(0);
        (await db.Assignments.Find(a => a.ShipmentId == shipment.ShipmentId).FirstAsync()).Status.Should().Be(AssignmentStatus.Completed);
    }

    [DockerFact]
    public async Task Manual_assignment_to_an_off_duty_courier_is_refused()
    {
        var db = await mongo.NewDatabaseAsync();
        var off = Courier("Off", 52.52, 13.41, available: false);
        await db.Couriers.InsertOneAsync(off);
        var shipment = AddShipment();

        var act = () => Service(db).AssignManualAsync(shipment.ShipmentId, off.Id, "dispatcher", default);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*off duty*");
    }

    [DockerFact]
    public async Task Parallel_offers_never_overfill_a_courier()
    {
        var db = await mongo.NewDatabaseAsync();
        var onlyOne = Courier("Single slot", 52.52, 13.41, max: 1);
        await db.Couriers.InsertOneAsync(onlyOne);
        var shipments = Enumerable.Range(0, 5).Select(_ => AddShipment()).ToList();
        foreach (var s in shipments)
        {
            await Service(db).QueueAsync(Created(s), default);
        }

        var results = await Task.WhenAll(shipments.Select(s => Service(db).OfferAsync(s.ShipmentId, default)));

        results.Count(r => r.Offered).Should().Be(1);
        (await Reload(db, onlyOne.Id)).ActiveCount.Should().Be(1);
    }

    [DockerFact]
    public async Task Redelivered_shipments_created_starts_only_one_saga()
    {
        var (db, service, shipment) = await QueuedAsync(Courier("A", 52.52, 13.41));

        await service.QueueAsync(Created(shipment), default);

        (await db.Sagas.CountDocumentsAsync(s => s.ShipmentId == shipment.ShipmentId)).Should().Be(1);
    }

    [DockerFact]
    public async Task Location_updates_only_move_forward_in_time()
    {
        var db = await mongo.NewDatabaseAsync();
        var courier = Courier("Sara", 52.52, 13.41);
        await db.Couriers.InsertOneAsync(courier);
        var service = Service(db);
        var now = _clock.GetUtcNow();

        await service.UpdateCourierLocationAsync(new LocationUpdated(courier.Id, 52.50, 13.40, 15, now, []), default);
        await service.UpdateCourierLocationAsync(new LocationUpdated(courier.Id, 52.40, 13.30, 15, now.AddMinutes(-5), []), default);

        (await Reload(db, courier.Id)).LastLocation!.Lat.Should().Be(52.50);
    }

    [DockerFact]
    public async Task The_unique_index_allows_only_one_active_assignment_per_shipment()
    {
        var db = await mongo.NewDatabaseAsync();
        var shipmentId = Guid.NewGuid();
        await db.Assignments.InsertOneAsync(new Assignment { ShipmentId = shipmentId, Status = AssignmentStatus.Active });
        await db.Assignments.InsertOneAsync(new Assignment { ShipmentId = shipmentId, Status = AssignmentStatus.Reassigned });

        var act = () => db.Assignments.InsertOneAsync(new Assignment { ShipmentId = shipmentId, Status = AssignmentStatus.Active });

        await act.Should().ThrowAsync<MongoWriteException>();
        await db.EnsureIndexesAsync(); // idempotent
    }

    private sealed class FakeDirectory : IShipmentDirectory
    {
        public Dictionary<Guid, ShipmentInfo> Shipments { get; } = [];

        public Task<ShipmentInfo?> GetAsync(Guid shipmentId, CancellationToken ct)
        {
            lock (Shipments)
            {
                return Task.FromResult(Shipments.GetValueOrDefault(shipmentId));
            }
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<(string Topic, string Key)> Published { get; } = [];

        public Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default)
        {
            lock (Published)
            {
                Published.Add((topic, key));
            }

            return Task.CompletedTask;
        }

        public Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? traceParent, CancellationToken ct = default) =>
            PublishAsync(topic, key, eventType, envelopeJson, ct);
    }
}
