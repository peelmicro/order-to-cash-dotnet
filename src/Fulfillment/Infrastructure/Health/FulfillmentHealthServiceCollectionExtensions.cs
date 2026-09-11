using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Fulfillment.Application.Ports;

namespace OrderToCash.Fulfillment.Infrastructure.Health;

/// <summary>
/// <c>AddFulfillmentHealth(IServiceCollection, Action&lt;HealthOptions&gt;)</c>
/// — design.md §8.2: Fulfillment checks <c>writeModel</c> (MS-SQL) and
/// <c>rpcTransport</c> (NATS) only — no Kafka check, no read model.
/// </summary>
public static class FulfillmentHealthServiceCollectionExtensions
{
    public static IServiceCollection AddFulfillmentHealth(this IServiceCollection services, Action<HealthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HealthOptions();
        configure(options);

        services.AddSingleton<IOptions<HealthOptions>>(Options.Create(options));

        services.AddSingleton<IHealthCheck, MsSqlHealthCheck>();
        services.AddSingleton<IHealthCheck, NatsHealthCheck>();

        services.AddSingleton<HealthProbeService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HealthProbeService>());

        return services;
    }
}
