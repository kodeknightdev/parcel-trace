using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ParcelTrace.BuildingBlocks.Kafka;

namespace ParcelTrace.BuildingBlocks.Telemetry;

public static class TelemetryExtensions
{
    public static WebApplicationBuilder AddParcelTelemetry(this WebApplicationBuilder builder, string serviceName, params string[] extraSources)
    {
        if (!builder.Configuration.GetValue("Otel:Enabled", true))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: builder.Configuration["OTEL_SERVICE_NAME"] ?? serviceName, serviceNamespace: "parcel-trace")
                .AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(KafkaTracing.SourceName)
                    .AddSource(extraSources)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/swagger");
                        o.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(o => o.RecordException = true)
                    .AddOtlpExporter();
            });

        return builder;
    }
}
