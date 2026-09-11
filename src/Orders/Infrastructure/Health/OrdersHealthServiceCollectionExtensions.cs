using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Health;

/// <summary>
/// <c>AddOrdersHealth(IServiceCollection, Action&lt;HealthOptions&gt;)</c> —
/// design.md §8.2: Orders checks <c>writeModel</c> (MS-SQL),
/// <c>factStream</c> (Kafka) and <c>rpcTransport</c> (NATS) — no
/// <c>readModel</c>, Orders owns no read model.
/// <see cref="Application.Ports.IHealthCheck"/> is resolved as
/// <c>IEnumerable&lt;IHealthCheck&gt;</c> by <see cref="HealthProbeService"/>,
/// one explicit registration line per check, no assembly scan.
/// </summary>
public static class OrdersHealthServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersHealth(this IServiceCollection services, Action<HealthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new HealthOptions();
        configure(options);

        services.AddSingleton<IOptions<HealthOptions>>(Options.Create(options));

        services.AddSingleton<IHealthCheck, MsSqlHealthCheck>();
        services.AddSingleton<IHealthCheck, KafkaHealthCheck>();
        services.AddSingleton<IHealthCheck, NatsHealthCheck>();

        // Registered both as itself (so a test can resolve the concrete
        // instance and read BoundPort after StartAsync) and as the SAME
        // singleton IHostedService — the OutboxRelay/IOutboxRelay dual
        // registration shape this service already establishes.
        services.AddSingleton<HealthProbeService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HealthProbeService>());

        return services;
    }
}
