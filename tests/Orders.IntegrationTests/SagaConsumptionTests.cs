using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>design.md §8.1 — SO1 (a fact published before the consumer group ever subscribed is still consumed) and SO9 (a handler that throws leaves the committed offset unchanged and the fact is redelivered).</summary>
[Collection(SagaCollection.Name)]
public sealed class SagaConsumptionTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    /// <summary><c>KafkaFactStreamSubscriber.BuildConsumerConfig</c>'s own <c>GroupId</c> literal — deliberately duplicated here rather than reached into <c>Infrastructure</c>, so SO9's assertion below reads the broker's own bookkeeping for the group the subscriber actually joins, not a constant re-exported for the test's convenience.</summary>
    private const string SagaGroupId = "orders.saga";

    /// <summary><see cref="KafkaContainerFixture"/> creates <c>otc.orders.facts.v1</c> with 6 partitions — kept in sync here because <see cref="SagaIntegrationTestSupport.ReadCommittedOffsetsAsync"/> needs the full partition set to sum a group's committed offset across the topic.</summary>
    private const int OrdersFactTopicPartitionCount = 6;

    [Fact]
    public async Task SO1_FirstBoot_ConsumesAFactPublishedBeforeTheConsumerGroupEverSubscribed()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_saga_so1_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(migrateDb);
        }

        Guid orderId;

        // First host: outbox + acceptance ONLY — no orders.saga consumer
        // group exists at all yet. orders.create places the order and the
        // relay publishes order.placed.v1 to Kafka while nobody in group
        // "orders.saga" has EVER subscribed to it.
        {
            var firstBuilder = Host.CreateApplicationBuilder();
            firstBuilder.Services.AddOrdersOutbox(options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 200;
            });
            firstBuilder.Services.AddOrdersAcceptance(options => options.Nats.Url = nats.Url);
            firstBuilder.Services.AddDispatcher(typeof(OrderToCash.Orders.Application.Commands.PlaceOrderCommand).Assembly);

            var firstHost = firstBuilder.Build();
            await firstHost.StartAsync();
            try
            {
                await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
                var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(firstHost);
                orderId = placed.OrderId.Value;

                Assert.Equal(1, await SagaIntegrationTestSupport.WaitForOutboxEventCountAsync(connectionString, mssql, orderId, "order.placed.v1", atLeast: 1, _wait));
            }
            finally
            {
                await firstHost.StopAsync();
                firstHost.Dispose();
            }
        }

        // Confirm no saga_commands row exists yet — the group never subscribed while order.placed.v1 sat on the topic.
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            Assert.Equal(0, await db.SagaCommands.CountAsync(c => c.OrderId == orderId));
        }

        // Second host, SAME database, fresh process, FULL saga wiring:
        // AutoOffsetReset.Earliest (SO1) means first boot consumes from the
        // beginning of the topic rather than skipping what is already there.
        var secondBuilder = OrderToCash.Orders.OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 200;
            },
            configureAcceptance: options => options.Nats.Url = nats.Url,
            configureSaga: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Command.TimeoutMs = 1_000;
                options.Command.BackoffMs = 100;
                options.Sweeper.IntervalMs = 500;
                options.Sweeper.PendingGraceMs = 300;
            });

        var secondHost = secondBuilder.Build();
        await secondHost.StartAsync();
        try
        {
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "pending", _wait);
        }
        finally
        {
            await secondHost.StopAsync();
            secondHost.Dispose();
        }
    }

    [Fact]
    public async Task SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered()
    {
        var connectionString = await BuildMigratedDatabaseAsync();

        var builder = OrderToCash.Orders.OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 200;
            },
            configureAcceptance: options => options.Nats.Url = nats.Url,
            configureSaga: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Command.TimeoutMs = 1_000;
                options.Command.BackoffMs = 100;
                options.Sweeper.IntervalMs = 500;
                options.Sweeper.PendingGraceMs = 300;
            });

        var gate = new ThrowOnceGate();
        builder.Services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped<ISagaCommandStore>(sp =>
            new ThrowOnceSagaCommandStore(new OrderToCash.Orders.Infrastructure.Saga.EfCoreSagaCommandStore(
                sp.GetRequiredService<OrderToCash.Orders.Infrastructure.Persistence.OrdersDbContext>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrderToCash.Orders.Infrastructure.OrdersSagaOptions>>()),
                gate)));

        var host = builder.Build();
        await host.StartAsync();
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(nats.Url, r => new StockReserveReplyPayload("accepted", r.OrderReference, Reservations: []), CancellationToken.None);

            // D1's repair: SO9 is proven from the "orders.saga" group's OWN
            // committed offset at the broker, never inferred from the
            // redelivery count alone — with EnableAutoCommit = false (the
            // library's alternative, but wrong, way to disable premature
            // commits) the redelivery still happens (no committed offset to
            // resume from, AutoOffsetReset.Earliest replays from the topic's
            // start), which is indistinguishable from the intended
            // behaviour if only the redelivery is observed. Baseline is
            // read BEFORE this order's own order.placed.v1 fact exists, so
            // both halves below compare against this test's own starting
            // point on a group shared, sequentially, by every test in
            // SagaCollection.
            var (baseline, baselineDescription) = await SagaIntegrationTestSupport.ReadCommittedOffsetsAsync(kafka.BootstrapServers, OrdersFactTopic.Name, SagaGroupId, OrdersFactTopicPartitionCount, TimeSpan.FromSeconds(10));

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // The FIRST delivery of order.placed.v1 throws inside the
            // transactional unit (EnqueueAsync) — the whole transaction
            // rolls back, so the offset is never stored and the SAME
            // message is redelivered.
            var deadline = DateTime.UtcNow + _wait;
            while (DateTime.UtcNow < deadline && gate.Attempts < 1)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 1, "the decorated store was never reached — the fact never arrived at all.");

            // Wait for the REDELIVERY to arrive and reach the gate — it is
            // now provably blocked BEFORE it can touch the inner store
            // (ThrowOnceGate.BeforeEnqueueAsync), so what happens next is
            // not a race against the retry's own timing.
            var redeliveryDeadline = DateTime.UtcNow + _wait;
            while (DateTime.UtcNow < redeliveryDeadline && gate.Attempts < 2)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 2, "the redelivery never reached the decorated store a second time within the wait budget.");

            // SO9's "not before" half, read from the broker: the redelivery
            // is blocked (deterministically, not by a wall-clock guess) and
            // has not been allowed to call the inner store, so nothing NEW
            // can legitimately be committed yet. KafkaFactStreamSubscriber's
            // `finally` calls consumer.Close() on the dying consumer the
            // instant the FIRST handler's exception propagated out of
            // ConsumeAsync, and Close() commits any STORED offset
            // immediately as part of leaving the group cleanly — so a
            // StoreOffset called too early (F6's two mutations:
            // EnableAutoOffsetStore = true, or StoreOffset moved before the
            // handler's await) would already be visible at the broker by
            // this point.
            var (afterFailedDelivery, afterFailedDescription) = await SagaIntegrationTestSupport.ReadCommittedOffsetsAsync(kafka.BootstrapServers, OrdersFactTopic.Name, SagaGroupId, OrdersFactTopicPartitionCount, TimeSpan.FromSeconds(10));
            Assert.True(baseline == afterFailedDelivery, $"the committed offset moved BEFORE the redelivery was allowed to succeed — baseline=[{baselineDescription}] afterFailedDelivery=[{afterFailedDescription}].");

            // Now let the redelivery proceed.
            gate.Release();

            // The SECOND delivery (the redelivery) succeeds — a saga_commands row eventually appears.
            var sentCount = await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            Assert.True(sentCount > 0);

            // SO9's "does advance" half, read from the broker: after the
            // successful redelivery, StoreOffset was called, and the next
            // auto.commit.interval.ms tick commits it — polled, bounded
            // well past one interval, rather than a fixed sleep.
            var afterSuccess = await SagaIntegrationTestSupport.WaitForCommittedOffsetToExceedAsync(kafka.BootstrapServers, OrdersFactTopic.Name, SagaGroupId, OrdersFactTopicPartitionCount, baseline, TimeSpan.FromSeconds(15));
            Assert.True(afterSuccess > baseline, $"the '{SagaGroupId}' group's committed offset on '{OrdersFactTopic.Name}' never advanced past {baseline} after the successful redelivery (last observed {afterSuccess}) — SO9's 'only after success' half is unproven.");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private async Task<string> BuildMigratedDatabaseAsync()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_saga_so9_{Guid.NewGuid():N}");
        await using var seedDb = mssql.CreateDbContext(connectionString);
        await seedDb.Database.MigrateAsync();
        await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        return connectionString;
    }

    /// <summary>
    /// D1's repair: the SECOND attempt (the redelivery) no longer proceeds
    /// the instant it arrives — it BLOCKS until the test explicitly
    /// <see cref="Release"/>s it. This turns "assert the committed offset
    /// is unchanged while the redelivery has not yet been allowed to
    /// succeed" from a wall-clock guess (how long can the redelivery
    /// possibly take?) into a deterministic ordering: the test observes
    /// <see cref="Attempts"/> reach 2 (the redelivery has arrived and is
    /// now provably blocked BEFORE it can store anything), reads the
    /// broker, THEN releases it. An earlier version of this test slept a
    /// fixed 7 s instead — long enough that the real redelivery (a ~2 s
    /// fixed retry delay in <c>SagaFactsConsumer</c>, plus a rejoin and a
    /// successful second attempt) had ALREADY completed and committed
    /// inside that window, so the "not before" assertion was comparing the
    /// wrong two points in time. Rewritten after that was caught by
    /// re-running the whole suite once, not by a mutation.
    /// </summary>
    private sealed class ThrowOnceGate
    {
        private int _attempts;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Attempts => Volatile.Read(ref _attempts);

        /// <summary>Called by the decorated store on EVERY <c>EnqueueAsync</c> attempt, BEFORE it touches the inner store. The first call throws immediately. Every call after that increments <see cref="Attempts"/> and then blocks on <see cref="Release"/> — the test's own signal, never a timer.</summary>
        public async Task BeforeEnqueueAsync(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException("SO9 test seam: simulated failure on first delivery.");
            }

            using var registration = cancellationToken.Register(() => _release.TrySetCanceled(cancellationToken));
            await _release.Task;
        }

        public void Release() => _release.TrySetResult();
    }

    /// <summary>Throws on the FIRST call to <see cref="EnqueueAsync"/> (inside the fact's own transaction, so it rolls back cleanly); every subsequent call blocks on <see cref="ThrowOnceGate.Release"/> before delegating — SO9's test seam.</summary>
    private sealed class ThrowOnceSagaCommandStore(ISagaCommandStore inner, ThrowOnceGate gate) : ISagaCommandStore
    {
        public async Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken)
        {
            await gate.BeforeEnqueueAsync(cancellationToken);
            return await inner.EnqueueAsync(orderId, orderReference, command, payload, triggeringEventId, cancellationToken);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => inner.TryClaimAsync(orderId, command, cancellationToken);

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => inner.ClaimDueAsync(batchSize, cancellationToken);

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => inner.MarkSentAsync(commandId, cancellationToken);

        public Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.ParkAsync(commandId, attemptsMade, lastError, cancellationToken);

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.RejectAsync(commandId, attemptsMade, lastError, cancellationToken);
    }
}
