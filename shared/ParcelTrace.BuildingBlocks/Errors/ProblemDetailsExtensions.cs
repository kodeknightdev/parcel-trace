using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ParcelTrace.BuildingBlocks.Errors;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddParcelProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
                ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<UnexpectedExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseAppExceptions(this IApplicationBuilder app) => app.UseMiddleware<AppExceptionMiddleware>();

    internal static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        return Results.Problem(problem).ExecuteAsync(context);
    }
}

internal sealed class AppExceptionMiddleware(RequestDelegate next, ILogger<AppExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted && exception is AppException or BadHttpRequestException or UnauthorizedAccessException)
        {
            var problem = exception switch
            {
                AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Message },
                BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message },
                _ => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" }
            };

            logger.LogInformation("{Method} {Path} -> {Status}: {Detail}", context.Request.Method, context.Request.Path, problem.Status, problem.Detail);
            context.Response.Clear();
            await ProblemDetailsExtensions.WriteAsync(context, problem);
        }
    }
}

internal sealed class UnexpectedExceptionHandler(ILogger<UnexpectedExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        await ProblemDetailsExtensions.WriteAsync(context, new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred",
            Detail = env.IsDevelopment() ? exception.Message : null
        });
        return true;
    }
}
