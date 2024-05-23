using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("gateway");
builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapReverseProxy();

app.Run();

public partial class Program;
