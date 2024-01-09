namespace ParcelTrace.Contracts;

public static class Topics
{
    public const string ShipmentCreated = "shipments.created";
    public const string ShipmentCancelled = "shipments.cancelled";
    public const string CourierAssigned = "dispatch.courier-assigned";
    public const string LocationUpdated = "tracking.location-updated";
    public const string StatusChanged = "tracking.status-changed";
    public const string EtaRecalculated = "eta.recalculated";

    public static string Retry(string group) => $"{group}.retry";

    public static string DeadLetter(string group) => $"{group}.dlq";
}

public static class EventTypes
{
    public const string ShipmentCreated = "ShipmentCreated";
    public const string ShipmentCancelled = "ShipmentCancelled";
    public const string CourierAssigned = "CourierAssigned";
    public const string LocationUpdated = "LocationUpdated";
    public const string StatusChanged = "StatusChanged";
    public const string EtaRecalculated = "EtaRecalculated";
}

public static class KafkaHeaders
{
    public const string EventType = "event-type";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string RetryCount = "retry-count";
    public const string OriginalTopic = "original-topic";
    public const string Error = "error";
}
