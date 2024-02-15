using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using ParcelTrace.Contracts;

namespace ParcelTrace.BuildingBlocks.Kafka;

// Confluent.Kafka has no tracing support, so traceparent goes through the headers by hand
public static class KafkaTracing
{
    public const string SourceName = "ParcelTrace.Kafka";

    private static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartPublish(string topic, string eventType, ActivityContext parent = default)
    {
        var activity = Source.StartActivity($"{topic} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("parcel.event_type", eventType);
        return activity;
    }

    public static Activity? StartProcess(string topic, string eventType, string consumerGroup, ActivityContext parent)
    {
        var activity = Source.StartActivity($"{topic} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.source.name", topic);
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.kafka.consumer.group", consumerGroup);
        activity?.SetTag("parcel.event_type", eventType);
        return activity;
    }

    public static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

    public static void Inject(Headers headers, Activity? activity, string? fallbackTraceParent = null)
    {
        var traceParent = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : fallbackTraceParent;
        if (string.IsNullOrEmpty(traceParent))
        {
            return;
        }

        headers.Remove(KafkaHeaders.TraceParent);
        headers.Add(KafkaHeaders.TraceParent, Encoding.UTF8.GetBytes(traceParent));
        if (!string.IsNullOrEmpty(activity?.TraceStateString))
        {
            headers.Remove(KafkaHeaders.TraceState);
            headers.Add(KafkaHeaders.TraceState, Encoding.UTF8.GetBytes(activity.TraceStateString));
        }
    }

    public static ActivityContext Extract(Headers? headers) =>
        Parse(GetHeader(headers, KafkaHeaders.TraceParent), GetHeader(headers, KafkaHeaders.TraceState));

    public static ActivityContext Parse(string? traceParent, string? traceState = null) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context)
            ? context
            : default;

    internal static string? GetHeader(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
