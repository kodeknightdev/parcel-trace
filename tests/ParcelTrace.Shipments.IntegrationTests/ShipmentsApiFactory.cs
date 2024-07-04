using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelTrace.Shipments.Eta;
using ParcelTrace.TestSupport;
using Testcontainers.PostgreSql;

namespace ParcelTrace.Shipments.IntegrationTests;

public sealed class ShipmentsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16.2-alpine")
        .WithDatabase("shipments")
        .Build();

    public FakeEtaClient Eta { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Shipments", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", TestTokens.SigningKey);
        builder.UseSetting("Kafka:Enabled", "false");
        builder.UseSetting("Otel:Enabled", "false");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:Seed", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEtaClient>();
            services.AddSingleton<IEtaClient>(Eta);
        });
    }

    public HttpClient ClientFor(Guid userId, string role, string email = "customer@parceltrace.local", Guid? courierId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, role, email, courierId));
        return client;
    }

    public async Task InitializeAsync()
    {
        if (DockerFactAttribute.IsDockerAvailable)
        {
            await _postgres.StartAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class FakeEtaClient : IEtaClient
{
    public EtaEstimate? Next { get; set; } = new(7.4, 31, DateTimeOffset.UtcNow.AddMinutes(31), 1.15);

    public Task<EtaEstimate?> EstimateAsync(EtaEstimateRequest request, CancellationToken ct) => Task.FromResult(Next);
}
