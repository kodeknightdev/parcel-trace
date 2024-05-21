using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;
using ParcelTrace.Shipments.Endpoints;
using ParcelTrace.Shipments.Eta;
using ParcelTrace.Shipments.Grpc;
using ParcelTrace.Shipments.Messaging;
using ParcelTrace.Shipments.Outbox;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("shipments", typeof(CreateShipmentRequestValidator));

var connectionString = builder.Configuration.GetConnectionString("Shipments")
                       ?? throw new InvalidOperationException("ConnectionStrings:Shipments is not configured.");
builder.Services.AddDbContext<ShipmentsDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddParcelKafka(builder.Configuration);
builder.Services.AddGrpc();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ShipmentOptions>(builder.Configuration.GetSection(ShipmentOptions.SectionName));
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection(PricingOptions.SectionName));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddSingleton(sp => new QuoteCalculator(
    sp.GetRequiredService<IOptions<PricingOptions>>().Value,
    sp.GetRequiredService<IOptions<ShipmentOptions>>().Value));
builder.Services.AddSingleton<ITrackingNumberGenerator, TrackingNumberGenerator>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<ShipmentEventHandler>();

builder.Services.AddHttpClient<IEtaClient, EtaClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Eta"] ?? "http://localhost:5105"))
    .AddStandardResilienceHandler(o =>
    {
        // nice to have, don't hold up booking
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(6);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    });

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<ShipmentsConsumer>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapShipmentEndpoints();
app.MapDashboardEndpoints();
app.MapGrpcService<ShipmentLookupService>();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Database.MigrateAsync();
    if (app.Configuration.GetValue("Database:Seed", true))
    {
        await Seed.RunAsync(scope.ServiceProvider, app.Logger);
    }
}

app.Run();

public partial class Program;
