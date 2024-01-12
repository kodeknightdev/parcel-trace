using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParcelTrace.Contracts;

namespace ParcelTrace.BuildingBlocks.Auth;

public static class AuthPolicies
{
    public const string Authenticated = "authenticated";
    public const string Customer = "customer";
    public const string Dispatcher = "dispatcher";
    public const string Courier = "courier";
    public const string Admin = "admin";
    public const string Staff = "staff";
}

public static class AuthExtensions
{
    public static IServiceCollection AddParcelJwt(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(s => s.SigningKey.Length >= 32, "Jwt:SigningKey must be configured and at least 32 characters long.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // read from IOptions so test overrides are picked up
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwt) =>
            {
                var settings = jwt.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ParcelClaims.Name,
                    RoleClaimType = ParcelClaims.Role
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Authenticated, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Customer, p => p.RequireRole(Roles.Customer, Roles.Admin))
            .AddPolicy(AuthPolicies.Dispatcher, p => p.RequireRole(Roles.Dispatcher, Roles.Admin))
            .AddPolicy(AuthPolicies.Courier, p => p.RequireRole(Roles.Courier))
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(Roles.Admin))
            .AddPolicy(AuthPolicies.Staff, p => p.RequireRole(Roles.Dispatcher, Roles.Courier, Roles.Admin));

        return services;
    }

    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new UnauthorizedAccessException("Token has no subject.");

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty;

    public static Guid? GetCourierId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ParcelClaims.CourierId), out var id) ? id : null;

    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Dispatcher) || user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Courier);

    public static bool IsDispatcherOrAdmin(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Dispatcher) || user.IsInRole(Roles.Admin);
}
