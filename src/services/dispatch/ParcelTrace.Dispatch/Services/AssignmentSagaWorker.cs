using Microsoft.Extensions.Options;

namespace ParcelTrace.Dispatch.Services;

public sealed class AssignmentSagaWorker(IServiceScopeFactory scopeFactory, IOptions<DispatchOptions> options, ILogger<AssignmentSagaWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SweepIntervalSeconds;
        if (interval <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AssignmentService>();

                var expired = await service.ExpireOffersAsync(stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("{Count} courier offers timed out", expired);
                }

                if (options.Value.AutoAssignOnCreate)
                {
                    var results = await service.OfferPendingAsync(stoppingToken);
                    var offered = results.Count(r => r.Offered);
                    if (offered > 0)
                    {
                        logger.LogInformation("Offered {Offered} of {Waiting} waiting shipments", offered, results.Count);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Assignment saga sweep failed");
            }
        }
    }
}
