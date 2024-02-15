using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParcelTrace.Contracts;

namespace ParcelTrace.BuildingBlocks.Kafka;

public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default);

    Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? traceParent, CancellationToken ct = default);
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string>? _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        if (!options.Value.Enabled)
        {
            return;
        }

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public static string Serialize<T>(string eventType, T payload) =>
        JsonSerializer.Serialize(EventEnvelope<T>.Create(eventType, payload), JsonDefaults.Options);

    public Task PublishAsync<T>(string topic, string key, string eventType, T payload, CancellationToken ct = default) =>
        PublishRawAsync(topic, key, eventType, Serialize(eventType, payload), KafkaTracing.CurrentTraceParent(), ct);

    public async Task PublishRawAsync(string topic, string key, string eventType, string envelopeJson, string? traceParent, CancellationToken ct = default)
    {
        if (_producer is null)
        {
            _logger.LogDebug("Kafka disabled, dropping {EventType} for {Topic}", eventType, topic);
            return;
        }

        using var activity = KafkaTracing.StartPublish(topic, eventType, KafkaTracing.Parse(traceParent));
        var headers = new Headers { { KafkaHeaders.EventType, Encoding.UTF8.GetBytes(eventType) } };
        KafkaTracing.Inject(headers, activity, traceParent);

        var result = await _producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = envelopeJson, Headers = headers }, ct);
        activity?.SetTag("messaging.kafka.destination.partition", result.Partition.Value);
        _logger.LogInformation("Published {EventType} to {Topic} [{Partition}@{Offset}] key {Key}",
            eventType, topic, result.Partition.Value, result.Offset.Value, key);
    }

    public void Dispose()
    {
        _producer?.Flush(TimeSpan.FromSeconds(5));
        _producer?.Dispose();
    }
}
