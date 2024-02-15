using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParcelTrace.Contracts;

namespace ParcelTrace.BuildingBlocks.Kafka;

public abstract class KafkaConsumerService(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> options, ILogger logger)
    : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;
    protected readonly ILogger Logger = logger;

    protected abstract IReadOnlyCollection<string> SubscribedTopics { get; }

    protected abstract Task HandleAsync(IServiceProvider services, string topic, RawEnvelope envelope, CancellationToken ct);

    private string RetryTopic => Topics.Retry(_options.GroupId);
    private string DeadLetterTopic => Topics.DeadLetter(_options.GroupId);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            Logger.LogWarning("Kafka disabled; {Consumer} will not start", GetType().Name);
            return Task.CompletedTask;
        }

        // Consume() blocks, keep it off the thread pool
        return Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false
        };

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _options.BootstrapServers, Acks = Acks.All }).Build();
        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => Logger.LogWarning("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        var topics = SubscribedTopics.Append(RetryTopic).ToList();
        consumer.Subscribe(topics);
        Logger.LogInformation("{Consumer} subscribed to {Topics}", GetType().Name, string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                Logger.LogWarning(ex, "Consume failed: {Reason}", ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            try
            {
                await ProcessAsync(result, producer, stoppingToken);
                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // couldn't even park it on retry/dlq - don't commit, try again
                Logger.LogError(ex, "Could not process or park message from {Topic}; it will be redelivered", result.Topic);
                consumer.Seek(result.TopicPartitionOffset);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        consumer.Close();
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, IProducer<string, string> producer, CancellationToken ct)
    {
        var headers = result.Message.Headers;
        var originalTopic = KafkaTracing.GetHeader(headers, KafkaHeaders.OriginalTopic) ?? result.Topic;
        var retryCount = int.TryParse(KafkaTracing.GetHeader(headers, KafkaHeaders.RetryCount), out var r) ? r : 0;

        RawEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RawEnvelope>(result.Message.Value, JsonDefaults.Options);
            if (envelope is null || envelope.EventId == Guid.Empty)
            {
                throw new JsonException("Envelope is missing eventId");
            }
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Poison message on {Topic} key {Key}; sending to {Dlq}", result.Topic, result.Message.Key, DeadLetterTopic);
            await ForwardAsync(producer, DeadLetterTopic, result.Message, originalTopic, retryCount, ex.Message, ct);
            return;
        }

        using var activity = KafkaTracing.StartProcess(originalTopic, envelope.EventType, _options.GroupId, KafkaTracing.Extract(headers));
        activity?.SetTag("messaging.message.id", envelope.EventId.ToString());
        if (retryCount > 0)
        {
            activity?.SetTag("parcel.retry_count", retryCount);
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await HandleAsync(scope.ServiceProvider, originalTopic, envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var next = retryCount + 1;
            var target = next > _options.MaxRetries ? DeadLetterTopic : RetryTopic;
            Logger.LogWarning(ex, "Handler failed for {EventType} {EventId} (attempt {Attempt}); forwarding to {Target}",
                envelope.EventType, envelope.EventId, next, target);
            if (target == RetryTopic)
            {
                // small backoff
                await Task.Delay(TimeSpan.FromMilliseconds(500 * next), ct);
            }

            await ForwardAsync(producer, target, result.Message, originalTopic, next, ex.Message, ct);
        }
    }

    private static async Task ForwardAsync(IProducer<string, string> producer, string topic, Message<string, string> message,
        string originalTopic, int retryCount, string error, CancellationToken ct)
    {
        var headers = new Headers();
        foreach (var h in message.Headers ?? [])
        {
            if (h.Key is KafkaHeaders.RetryCount or KafkaHeaders.OriginalTopic or KafkaHeaders.Error)
            {
                continue;
            }

            headers.Add(h.Key, h.GetValueBytes());
        }

        headers.Add(KafkaHeaders.OriginalTopic, Encoding.UTF8.GetBytes(originalTopic));
        headers.Add(KafkaHeaders.RetryCount, Encoding.UTF8.GetBytes(retryCount.ToString()));
        headers.Add(KafkaHeaders.Error, Encoding.UTF8.GetBytes(error.Length > 500 ? error[..500] : error));

        await producer.ProduceAsync(topic, new Message<string, string> { Key = message.Key, Value = message.Value, Headers = headers }, ct);
    }

    protected static T Payload<T>(RawEnvelope envelope) =>
        envelope.Payload.Deserialize<T>(JsonDefaults.Options) ?? throw new JsonException($"Payload for {envelope.EventType} was empty");
}
