using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("gateway");
builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddGatewayRateLimiting(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHttpClient(nameof(DownstreamHealthCheck), c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHealthChecks().AddCheck<DownstreamHealthCheck>("downstream", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwaggerUI(o =>
{
    // service docs come through the swagger-* routes
    o.RoutePrefix = "swagger";
    o.DocumentTitle = "parcel-trace API";
    o.SwaggerEndpoint("/swagger/docs/identity.json", "identity");
    o.SwaggerEndpoint("/swagger/docs/shipments.json", "shipments");
    o.SwaggerEndpoint("/swagger/docs/dispatch.json", "dispatch");
    o.SwaggerEndpoint("/swagger/docs/tracking.json", "tracking (Node)");
    o.SwaggerEndpoint("/swagger/docs/eta.json", "eta (Python)");
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapParcelHealth();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapReverseProxy();

app.Run();

public partial class Program;
