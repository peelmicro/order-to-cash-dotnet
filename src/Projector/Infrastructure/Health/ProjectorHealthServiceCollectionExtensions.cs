using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Health;

/// <summary><c>AddProjectorHealth(IServiceCollection, Action&lt;HealthOptions&gt;)</c> — design.md §8.2: Projector checks <c>factStream</c> (Kafka), <c>rpcTransport</c> (NATS) and <c>readModel</c> (MongoDB).</summary>
public static class ProjectorHealthServiceCollectionExtensions
{
    public static IServiceCollection AddProjectorHealth(this IServiceCollection services, Action<HealthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HealthOptions();
        configure(options);

        services.AddSingleton<IOptions<HealthOptions>>(Options.Create(options));

        services.AddSingleton<IHealthCheck, KafkaHealthCheck>();
        services.AddSingleton<IHealthCheck, NatsHealthCheck>();
        services.AddSingleton<IHealthCheck, MongoHealthCheck>();

        services.AddSingleton<HealthProbeService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HealthProbeService>());

        return services;
    }
}
