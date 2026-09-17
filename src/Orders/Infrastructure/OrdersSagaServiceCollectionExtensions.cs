using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.DeadLetter;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Observability;
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

        // Backlog id 90, bullet 3 — FAIL FAST, never clamp silently, for a
        // value that arrived through configuration. CLAUDE.md's standing rule
        // is that DI and configuration failures must be LOUD AT BOOT, and this
        // particular value has the worst possible quiet failure. At exactly 0,
        // Enumerable.Range(0, 0) leaves
        // SagaCommandDispatchWorker.ExecuteAsync's Task.WhenAll with nothing to
        // wait for, the BackgroundService finishes SUCCESSFULLY, the Generic
        // Host stays up, /health/ready still answers 200 "up" (no health check
        // references this worker), and the fast path dispatches nothing for any
        // order, forever — every saga command silently demoted to the 30 s
        // sweeper. A clamp to 1 would hide the operator's mistake behind
        // degraded-but-working behaviour; a throw here stops the host before it
        // can ever report healthy.
        //
        // The Math.Max(1, ...) clamp in the worker itself is NOT redundant and
        // was deliberately kept: it is the floor for any path that constructs
        // the worker WITHOUT going through this composition root (tests, and
        // any future in-code wiring), which this validation cannot see. Belt
        // here, braces there, and both are armed — see
        // progress/impl_batch_d5_doc_comment_targets_and_dispatch_clamp.md.
        //
        // The predicate is "< 1" rather than "== 0" on purpose: 0 is the value
        // with the silent-healthy failure (a negative makes ExecuteAsync's task
        // FAULT instead, which a real host's default
        // BackgroundServiceExceptionBehavior.StopHost does notice), but a
        // negative is just as wrong and must be rejected by NAME here rather
        // than absorbed by the worker's clamp (Math.Max(1, -3) == 1) and never
        // mentioned again. Measured, both cases, while arming this entry.
        if (options.Dispatch.DegreeOfParallelism < 1)
        {
            throw new InvalidOperationException(
                $"OrdersSagaOptions.Dispatch.DegreeOfParallelism must be at least 1; it was configured as {options.Dispatch.DegreeOfParallelism}. " +
                "A value below 1 would give SagaCommandDispatchWorker no consumer loops at all: at 0 its ExecuteAsync finishes successfully, " +
                "the host keeps running and reports healthy, and every order's saga command silently falls back to the 30 s sweeper. " +
                "Backlog id 90.");
        }

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

        // OR1 — the retry-then-dead-letter wrapper (design.md §3). All
        // singletons: FactRetryDispatcher holds no scoped state of its own,
        // matching IdempotentConsumer's precedent one line above SagaFactsConsumer's
        // own registration below (a singleton BackgroundService may not
        // directly depend on a scoped service).
        services.AddSingleton<IOptions<FactRetryOptions>>(Options.Create(options.FactRetry));

        // Backlog id 104 — DeadLetter.BootstrapServers falls back to the
        // saga's OWN Kafka.BootstrapServers when not set explicitly, so a
        // caller that configures Kafka once (most test hosts) also
        // configures the dead-letter producer, rather than silently
        // defaulting to localhost:9092 regardless of which broker is under
        // test. OrdersProgramConfiguration.ConfigureSaga still sets
        // DeadLetter.BootstrapServers explicitly, so production behaviour is
        // unchanged in effect.
        if (string.IsNullOrEmpty(options.DeadLetter.BootstrapServers))
        {
            options.DeadLetter.BootstrapServers = options.Kafka.BootstrapServers;
        }

        services.AddSingleton<IOptions<DeadLetterKafkaOptions>>(Options.Create(options.DeadLetter));
        services.AddSingleton<IFactRetryDelay, TaskDelayFactRetryDelay>();
        services.AddSingleton<IDeadLetterPublisher, KafkaDeadLetterPublisher>();
        services.AddSingleton<FactRetryDispatcher>();

        // The fakeable seam over the existing, unmodified IdempotentConsumer
        // (design.md §5.1) — resolves ConsumerName.OrdersSaga internally.
        services.AddScoped<IIdempotentSagaRunner, IdempotentConsumerSagaRunner>();

        // OR3's first-park hook (design.md §4.4) — scoped, since it depends
        // on the ambient scoped IUnitOfWork/IOrderRepository, exactly as
        // SagaCommandDispatcher itself is.
        services.AddScoped<ISagaFirstParkDeadLetterHandler, SagaFirstParkDeadLetterHandler>();

        // OR5/design.md §7 — otc_saga_completion_ms.
        services.AddSingleton<ISagaCompletionRecorder, SagaCompletionRecorder>();

        // Feature 76 (application_layer_depends_on_infrastructure_unguarded)
        // — the port that lets Application build a saga command's/the
        // synthetic orders.cancel.requested envelope's wire body without
        // depending on Infrastructure.Messaging.Rpc.RpcJson directly.
        services.AddScoped<IRpcRequestSerializer, RpcJsonRequestSerializer>();
        services.AddScoped<Application.Sagas.SagaCommandRequestFactory>();

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
