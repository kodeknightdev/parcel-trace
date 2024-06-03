using Microsoft.AspNetCore.Authentication.Cookies;
using ParcelTrace.BuildingBlocks.Health;
using ParcelTrace.BuildingBlocks.Logging;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddParcelLogging("frontend");
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "parcel-trace.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccessDenied";
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("customer", p => p.RequireRole(Roles.Customer, Roles.Admin))
    .AddPolicy("dispatcher", p => p.RequireRole(Roles.Dispatcher, Roles.Admin))
    .AddPolicy("courier", p => p.RequireRole(Roles.Courier))
    .AddPolicy("admin", p => p.RequireRole(Roles.Admin));

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Shipments", "customer");
    o.Conventions.AuthorizeFolder("/Dispatch", "dispatcher");
    o.Conventions.AuthorizeFolder("/Courier", "courier");
    o.Conventions.AuthorizeFolder("/Admin", "admin");
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<AccessTokenHandler>();
builder.Services.AddSingleton<ICookieSignIn, CookieSignIn>();
builder.Services.AddHttpClient<IGatewayClient, GatewayClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://localhost:8080"))
    .AddHttpMessageHandler<AccessTokenHandler>()
    .AddStandardResilienceHandler();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapParcelHealth();
app.MapRazorPages();

app.Run();

public partial class Program;
