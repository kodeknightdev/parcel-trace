using MongoDB.Driver;
using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("dispatch", typeof(CourierRequestValidator));

builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));
var mongo = builder.Configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>() ?? new MongoOptions();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo.ConnectionString));
builder.Services.AddSingleton<DispatchDb>();
builder.Services.AddHealthChecks().AddMongoDb(mongo.ConnectionString, name: "mongodb", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapCourierEndpoints();

var db = app.Services.GetRequiredService<DispatchDb>();
await db.EnsureIndexesAsync();

app.Run();

public partial class Program;
