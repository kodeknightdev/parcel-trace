using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParcelTrace.BuildingBlocks.Health;

namespace ParcelTrace.BuildingBlocks.Kafka;

public static class KafkaExtensions
{
    public static IServiceCollection AddParcelKafka(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(KafkaOptions.SectionName);
        services.Configure<KafkaOptions>(section);
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();

        var options = section.Get<KafkaOptions>() ?? new KafkaOptions();
        if (options.Enabled)
        {
            services.AddHealthChecks().AddKafka(
                new ProducerConfig { BootstrapServers = options.BootstrapServers, MessageTimeoutMs = 3_000 },
                topic: options.HealthCheckTopic,
                name: "kafka",
                tags: [HealthEndpoints.ReadyTag],
                timeout: TimeSpan.FromSeconds(5));
        }

        return services;
    }
}
