using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Logging;
using ParcelTrace.BuildingBlocks.Telemetry;
using Serilog;
using Serilog.Events;

namespace ParcelTrace.BuildingBlocks;

public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName, Type? validatorsAssemblyMarker = null)
    {
        builder.AddParcelLogging(serviceName);
        builder.AddParcelTelemetry(serviceName);
        // enums as strings on the wire
        builder.Services.AddParcelProblemDetails();
        builder.Services.AddParcelSwagger($"parcel-trace {serviceName}");
        builder.Services.AddHealthChecks();
        if (validatorsAssemblyMarker is not null)
        {
            builder.Services.AddValidatorsFromAssemblyContaining(validatorsAssemblyMarker);
        }

        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
            ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information);
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
