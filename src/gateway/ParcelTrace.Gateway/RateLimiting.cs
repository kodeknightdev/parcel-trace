using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ParcelTrace.Gateway;

public static class RateLimitPolicies
{
    public const string Public = "public";
    public const string Login = "login";
    public const string Api = "api";

    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        var publicPerMinute = section.GetValue("PublicPerMinute", 60);
        var loginPerMinute = section.GetValue("LoginPerMinute", 10);
        var apiPerMinute = section.GetValue("ApiPerMinute", 300);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests",
                    detail: "Slow down and try again shortly.").ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(Public, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

            options.AddPolicy(Login, ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

            options.AddPolicy(Api, ctx =>
            {
                var key = ctx.User.FindFirstValue("sub") is { } sub ? $"user:{sub}" : $"ip:{ClientIp(ctx)}";
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = apiPerMinute,
                    TokensPerPeriod = apiPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });
    }

    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
