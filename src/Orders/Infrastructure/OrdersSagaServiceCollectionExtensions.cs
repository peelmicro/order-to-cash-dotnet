using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Saga;
using OrderToCash.Orders.Presentation;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>
/// <c>AddOrdersSaga(IServiceCollection, Action&lt;OrdersSagaOptions&gt;)</c> —
/// one explicit registration line per port (design.md §9, CLAUDE.md), no
/// assembly scan. Reuses the EXISTING singleton <see cref="INatsConnection"/>
/// (<c>AddOrdersAcceptance</c>), the existing scoped <c>OrdersDbContext</c>/
/// <see cref="IClock"/>/<see cref="IUnitOfWork"/> (<c>AddOrdersOutbox</c>) —
/// no second NATS connection, no second <c>DbContext</c> registration.
/// </summary>
public static class OrdersSagaServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersSaga(this IServiceCollection services, Action<OrdersSagaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OrdersSagaOptions();
        configure(options);

        services.AddSingleton<IOptions<OrdersSagaOptions>>(Options.Create(options));

        // IFactStreamSubscriber: SINGLETON, not scoped — SagaFactsConsumer is
        // itself a singleton BackgroundService (every AddHostedService is),
        // and ValidateOnBuild refuses a singleton that directly consumes a
        // scoped service (caught live while wiring this feature: "Cannot
        // consume scoped service ... from singleton"). KafkaFactStreamSubscriber
        // holds no scoped state of its own — the IConsumer client it builds
        // is created fresh INSIDE ConsumeAsync via `using` and disposed with
        // it, independent of this registration's lifetime — so singleton
        // loses nothing design.md's "created per ConsumeAsync call and
        // disposed with it" actually asked for.
        services.AddSingleton<IFactStreamSubscriber, KafkaFactStreamSubscriber>();
        services.AddScoped<ISagaCommands, NatsSagaCommandsAdapter>();

        // Persistence ports — over the ambient scoped OrdersDbContext, exactly
        // as EfCoreOrderRepository already does (no `tx` parameter anywhere).
        services.AddScoped<ISagaCommandStore, EfCoreSagaCommandStore>();
        services.AddScoped<ISagaIgnoredFactRecorder, EfCoreSagaIgnoredFactRecorder>();

        // The in-process fast-path signal — SINGLETON, because it owns the
        // channel. Dual-registered (ChannelSagaCommandSignal.cs) so the
        // dispatch worker can read from the SAME instance the event handlers
        // write to (OutboxRelay/IOutboxRelay's own dual-registration shape).
        services.AddSingleton<ChannelSagaCommandSignal>();
        services.AddSingleton<ISagaCommandSignal>(sp => sp.GetRequiredService<ChannelSagaCommandSignal>());

        services.AddSingleton<ISagaRetryDelay, TaskDelaySagaRetryDelay>();

        // The fakeable seam over the existing, unmodified IdempotentConsumer
        // (design.md §5.1) — resolves ConsumerName.OrdersSaga internally.
        services.AddScoped<IIdempotentSagaRunner, IdempotentConsumerSagaRunner>();

        // The transactional unit and the RPC issuer.
        services.AddScoped<Application.Sagas.SagaFactHandler>();
        services.AddScoped<ISagaCommandDispatcher, SagaCommandDispatcher>();
        services.AddScoped<ISagaCommandSweeper, SagaCommandSweeper>();

        // Three BackgroundServices — one per transport/loop (CLAUDE.md).
        services.AddHostedService<SagaFactsConsumer>();
        services.AddHostedService<SagaCommandDispatchWorker>();
        services.AddHostedService<SagaCommandSweeperBackgroundService>();

        return services;
    }
}
