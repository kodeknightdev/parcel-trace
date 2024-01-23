using Microsoft.EntityFrameworkCore;
using ParcelTrace.BuildingBlocks;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.Identity.Auth;
using ParcelTrace.Identity.Data;
using ParcelTrace.Identity.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("identity", typeof(RegisterRequestValidator));

var connectionString = builder.Configuration.GetConnectionString("Identity")
                       ?? throw new InvalidOperationException("ConnectionStrings:Identity is not configured.");
builder.Services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddParcelJwt(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapAuthEndpoints();
app.MapUserEndpoints();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();
    await Seed.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), app.Configuration, app.Logger);
}

app.Run();

public partial class Program;
