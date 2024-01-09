using System.Text.Json;

namespace ParcelTrace.Contracts;

public sealed record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    T Payload)
{
    public static EventEnvelope<T> Create(string eventType, T payload, int schemaVersion = 1) =>
        new(Guid.NewGuid(), eventType, schemaVersion, DateTimeOffset.UtcNow, payload);
}

public sealed record RawEnvelope(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    JsonElement Payload);
