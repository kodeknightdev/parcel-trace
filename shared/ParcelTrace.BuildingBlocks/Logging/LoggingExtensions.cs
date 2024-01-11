using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace ParcelTrace.BuildingBlocks.Logging;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddParcelLogging(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((ctx, _, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.With<TraceContextEnricher>()
                .Enrich.WithProperty("Service", serviceName);

            if (ctx.Configuration["Logging:Format"] == "text")
            {
                cfg.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}");
            }
            else
            {
                cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        return builder;
    }
}

public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
