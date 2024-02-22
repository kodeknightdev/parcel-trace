using Microsoft.Extensions.Options;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Messaging;

public sealed class ShipmentsConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<ShipmentsConsumer> logger)
    : KafkaConsumerService(scopeFactory, options, logger)
{
    protected override IReadOnlyCollection<string> SubscribedTopics { get; } =
        [Topics.CourierAssigned, Topics.StatusChanged, Topics.EtaRecalculated];

    protected override Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct)
    {
        var handler = services.GetRequiredService<ShipmentEventHandler>();
        return topic switch
        {
            Topics.CourierAssigned => handler.HandleCourierAssignedAsync(envelope.EventId, Payload<CourierAssigned>(envelope), ct),
            Topics.StatusChanged => handler.HandleStatusChangedAsync(envelope.EventId, Payload<StatusChanged>(envelope), ct),
            Topics.EtaRecalculated => handler.HandleEtaRecalculatedAsync(envelope.EventId, Payload<EtaRecalculated>(envelope), ct),
            _ => Task.CompletedTask
        };
    }
}
