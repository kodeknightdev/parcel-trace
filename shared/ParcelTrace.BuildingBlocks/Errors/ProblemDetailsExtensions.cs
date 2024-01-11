using System.Diagnostics;
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
        services.AddExceptionHandler<AppExceptionHandler>();
        return services;
    }
}

internal sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var problem = exception switch
        {
            AppException app => new ProblemDetails { Status = app.StatusCode, Title = app.Title, Detail = app.Message },
            BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = bad.Message },
            UnauthorizedAccessException => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = env.IsDevelopment() ? exception.Message : null
            }
        };

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("{Method} {Path} -> {Status}: {Detail}", context.Request.Method, context.Request.Path, problem.Status, problem.Detail);
        }

        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        await Results.Problem(problem).ExecuteAsync(context);
        return true;
    }
}
