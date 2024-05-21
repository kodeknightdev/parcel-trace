using System.Net.Http.Json;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Eta;

public sealed record EtaEstimateRequest(GeoPoint Origin, GeoPoint Destination, ServiceLevel ServiceLevel, DateTimeOffset? DepartAt = null);

public sealed record EtaEstimate(double DistanceKm, int Minutes, DateTimeOffset EtaAt, double TrafficFactor);

public interface IEtaClient
{
    Task<EtaEstimate?> EstimateAsync(EtaEstimateRequest request, CancellationToken ct);
}

public sealed class EtaClient(HttpClient http, ILogger<EtaClient> logger) : IEtaClient
{
    public async Task<EtaEstimate?> EstimateAsync(EtaEstimateRequest request, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/api/eta/estimate", request, JsonDefaults.Options, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<EtaEstimate>(JsonDefaults.Options, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            logger.LogWarning(ex, "ETA estimate unavailable, continuing without one");
            return null;
        }
    }
}
