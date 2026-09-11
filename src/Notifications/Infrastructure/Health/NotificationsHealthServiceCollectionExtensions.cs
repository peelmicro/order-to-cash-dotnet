using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Health;

/// <summary><c>AddNotificationsHealth(IServiceCollection, Action&lt;HealthOptions&gt;)</c> — design.md §8.2: Notifications checks <c>writeModel</c> (MS-SQL) and <c>factStream</c> (Kafka) only.</summary>
public static class NotificationsHealthServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsHealth(this IServiceCollection services, Action<HealthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HealthOptions();
        configure(options);

        services.AddSingleton<IOptions<HealthOptions>>(Options.Create(options));

        services.AddSingleton<IHealthCheck, MsSqlHealthCheck>();
        services.AddSingleton<IHealthCheck, KafkaHealthCheck>();

        services.AddSingleton<HealthProbeService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HealthProbeService>());

        return services;
    }
}
