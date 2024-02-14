using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;
using ParcelTrace.Shipments.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("shipments", typeof(CreateShipmentRequestValidator));

var connectionString = builder.Configuration.GetConnectionString("Shipments")
                       ?? throw new InvalidOperationException("ConnectionStrings:Shipments is not configured.");
builder.Services.AddDbContext<ShipmentsDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddParcelJwt(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ShipmentOptions>(builder.Configuration.GetSection(ShipmentOptions.SectionName));
builder.Services.Configure<PricingOptions>(builder.Configuration.GetSection(PricingOptions.SectionName));
builder.Services.AddSingleton(sp => new QuoteCalculator(
    sp.GetRequiredService<IOptions<PricingOptions>>().Value,
    sp.GetRequiredService<IOptions<ShipmentOptions>>().Value));
builder.Services.AddSingleton<ITrackingNumberGenerator, TrackingNumberGenerator>();
builder.Services.AddScoped<ShipmentService>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapShipmentEndpoints();
app.MapDashboardEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Database.MigrateAsync();
}

app.Run();

public partial class Program;
