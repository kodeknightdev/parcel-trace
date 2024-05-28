using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace ParcelTrace.Gateway;

public sealed class DownstreamHealthCheck(IProxyConfigProvider proxyConfig, IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(nameof(DownstreamHealthCheck));
        var destinations = proxyConfig.GetConfig().Clusters
            .SelectMany(c => (c.Destinations ?? new Dictionary<string, DestinationConfig>()).Select(d => (Cluster: c.ClusterId, d.Value.Address)))
            .ToList();

        var results = await Task.WhenAll(destinations.Select(async d =>
        {
            try
            {
                using var response = await client.GetAsync(new Uri(new Uri(d.Address), "/health"), cancellationToken);
                return (d.Cluster, Ok: response.IsSuccessStatusCode, Error: response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (d.Cluster, Ok: false, Error: ex.Message);
            }
        }));

        var data = results.ToDictionary(r => r.Cluster, r => (object)(r.Ok ? "Healthy" : $"Unhealthy: {r.Error}"));
        var failed = results.Where(r => !r.Ok).Select(r => r.Cluster).ToList();
        return failed.Count == 0
            ? HealthCheckResult.Healthy($"{results.Length} downstream services healthy", data)
            : HealthCheckResult.Unhealthy($"Unhealthy: {string.Join(", ", failed)}", data: data);
    }
}
