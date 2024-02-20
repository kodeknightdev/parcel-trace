using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Shipments.Data;

namespace ParcelTrace.Shipments.Outbox;

public static class OutboxWriter
{
    public static void Enqueue<T>(this ShipmentsDbContext db, string topic, string key, string eventType, T payload) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            Topic = topic,
            Key = key,
            EventType = eventType,
            Payload = KafkaEventPublisher.Serialize(eventType, payload),
            TraceParent = KafkaTracing.CurrentTraceParent()
        });
}
