using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.Health;

/// <summary><c>AddBillingHealth(IServiceCollection, Action&lt;HealthOptions&gt;)</c> — design.md §8.2: Billing checks <c>writeModel</c> (MS-SQL) and <c>rpcTransport</c> (NATS) only.</summary>
public static class BillingHealthServiceCollectionExtensions
{
    public static IServiceCollection AddBillingHealth(this IServiceCollection services, Action<HealthOptions> configure)
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
