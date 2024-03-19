using MongoDB.Driver;
using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Endpoints;
using ParcelTrace.Dispatch.Messaging;
using ParcelTrace.Dispatch.Services;
using ParcelTrace.Dispatch.Shipments;
using ParcelTrace.Protos.Shipments;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("dispatch", typeof(CourierRequestValidator));

builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
builder.Services.Configure<DispatchOptions>(builder.Configuration.GetSection(DispatchOptions.SectionName));
var mongo = builder.Configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>() ?? new MongoOptions();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo.ConnectionString));
builder.Services.AddSingleton<DispatchDb>();
builder.Services.AddHealthChecks().AddMongoDb(mongo.ConnectionString, name: "mongodb", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddParcelKafka(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddGrpcClient<ShipmentLookup.ShipmentLookupClient>(o =>
        o.Address = new Uri(builder.Configuration["Services:ShipmentsGrpc"] ?? "http://localhost:5112"))
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
    });
builder.Services.AddScoped<IShipmentDirectory, GrpcShipmentDirectory>();
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<DispatchEventHandler>();

builder.Services.AddHostedService<DispatchConsumer>();
builder.Services.AddHostedService<AssignmentSagaWorker>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapCourierEndpoints();
app.MapDispatchEndpoints();

var db = app.Services.GetRequiredService<DispatchDb>();
await db.EnsureIndexesAsync();
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:Seed", true))
{
    await Seed.RunAsync(db, app.Logger);
}

app.Run();

public partial class Program;
