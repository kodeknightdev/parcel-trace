using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Domain;
using ParcelTrace.Dispatch.Services;

namespace ParcelTrace.Dispatch.Messaging;

public sealed class DispatchEventHandler(DispatchDb db, AssignmentService assignments, IOptions<DispatchOptions> options, ILogger<DispatchEventHandler> logger)
{
    public const string ConsumerName = "dispatch";

    public async Task HandleAsync(RawEnvelope envelope, object payload, CancellationToken ct)
    {
        var key = $"{ConsumerName}:{envelope.EventId}";
        if (await db.Processed.Find(p => p.Id == key).AnyAsync(ct))
        {
            logger.LogDebug("Event {EventId} already processed", envelope.EventId);
            return;
        }

        switch (payload)
        {
            case ShipmentCreated created:
                await assignments.QueueAsync(created, ct);
                if (options.Value.AutoAssignOnCreate)
                {
                    var result = await assignments.OfferAsync(created.ShipmentId, ct);
                    if (!result.Offered)
                    {
                        logger.LogInformation("{TrackingNumber} waits in the queue: {Reason}", created.TrackingNumber, result.Reason);
                    }
                }

                break;
            case ShipmentCancelled cancelled:
                await assignments.CancelAsync(cancelled, ct);
                break;
            case StatusChanged changed:
                await assignments.CompleteAsync(changed, ct);
                break;
            case LocationUpdated location:
                await assignments.UpdateCourierLocationAsync(location, ct);
                break;
        }

        await db.Processed.ReplaceOneAsync(p => p.Id == key, new ProcessedEvent { Id = key }, new ReplaceOptions { IsUpsert = true }, ct);
    }
}

public sealed class DispatchConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger<DispatchConsumer> logger)
    : KafkaConsumerService(scopeFactory, options, logger)
{
    protected override IReadOnlyCollection<string> SubscribedTopics { get; } =
        [Topics.ShipmentCreated, Topics.ShipmentCancelled, Topics.StatusChanged, Topics.LocationUpdated];

    protected override Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct)
    {
        object payload = topic switch
        {
            Topics.ShipmentCreated => Payload<ShipmentCreated>(envelope),
            Topics.ShipmentCancelled => Payload<ShipmentCancelled>(envelope),
            Topics.StatusChanged => Payload<StatusChanged>(envelope),
            Topics.LocationUpdated => Payload<LocationUpdated>(envelope),
            _ => throw new InvalidOperationException($"Unexpected topic {topic}")
        };
        return services.GetRequiredService<DispatchEventHandler>().HandleAsync(envelope, payload, ct);
    }
}
