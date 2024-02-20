using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Shipments.Data;

namespace ParcelTrace.Shipments.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalMs { get; set; } = 1000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 20;
    public int RetentionDays { get; set; } = 7;
}

public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<OutboxOptions> options,
    IOptions<KafkaOptions> kafka,
    TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!kafka.Value.Enabled)
        {
            logger.LogWarning("Kafka disabled; outbox publisher not started");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishBatchAsync(stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox relay iteration failed");
            }

            // keep going while there's a backlog
            if (published < _options.BatchSize)
            {
                await Task.Delay(_options.PollIntervalMs, stoppingToken);
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED so several replicas don't pick up the same rows
        var batch = await db.OutboxMessages
            .FromSqlRaw("""
                        SELECT * FROM outbox_messages
                        WHERE "PublishedAt" IS NULL AND "Attempts" < {0}
                        ORDER BY "CreatedAt"
                        LIMIT {1}
                        FOR UPDATE SKIP LOCKED
                        """, _options.MaxAttempts, _options.BatchSize)
            .ToListAsync(ct);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishRawAsync(message.Topic, message.Key, message.EventType, message.Payload, message.TraceParent, ct);
                message.PublishedAt = clock.GetUtcNow();
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogWarning(ex, "Publishing outbox message {Id} ({EventType}) failed, attempt {Attempt}", message.Id, message.EventType, message.Attempts);
                break;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return published;
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now - _lastCleanup < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastCleanup = now;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        var cutoff = now.AddDays(-_options.RetentionDays);
        var deleted = await db.OutboxMessages.Where(m => m.PublishedAt != null && m.PublishedAt < cutoff).ExecuteDeleteAsync(ct);
        deleted += await db.ProcessedMessages.Where(m => m.ProcessedAt < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            logger.LogInformation("Removed {Count} old outbox/processed rows", deleted);
        }
    }
}
