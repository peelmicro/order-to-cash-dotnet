using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>design.md §8.1 — SO1 (a fact published before the consumer group ever subscribed is still consumed) and SO9 (a handler that throws leaves the committed offset unchanged until the retry succeeds).</summary>
[Collection(SagaCollection.Name)]
public sealed class SagaConsumptionTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    /// <summary><c>KafkaFactStreamSubscriber.BuildConsumerConfig</c>'s own <c>GroupId</c> literal — deliberately duplicated here rather than reached into <c>Infrastructure</c>, so SO9's assertion below reads the broker's own bookkeeping for the group the subscriber actually joins, not a constant re-exported for the test's convenience.</summary>
    private const string SagaGroupId = "orders.saga";

    /// <summary><see cref="KafkaContainerFixture"/> creates <c>otc.orders.facts.v1</c> with 6 partitions — kept in sync here because <see cref="SagaIntegrationTestSupport.ReadCommittedOffsetsAsync"/> needs the full partition set to sum a group's committed offset across the topic.</summary>
    private const int OrdersFactTopicPartitionCount = 6;

    /// <summary>
    /// Backlog id 94's repair. librdkafka's own default
    /// <c>auto.commit.interval.ms</c> is 5 000 (<c>KafkaFactStreamSubscriber</c>'s
    /// own class remarks cite the same figure from the pinned package's XML
    /// docs) — the periodic background committer that would turn a
    /// PREMATURELY STORED offset (F6's two mutations) into a COMMITTED one
    /// even though nothing ever calls <c>consumer.Close()</c> on this path
    /// (see the "not before" comment below for why <c>Close()</c> is no
    /// longer reachable here). SO9's "not before" half below polls for a
    /// window comfortably past one full tick of that interval — margin
    /// chosen empirically, not tightened to the minimum, since a container
    /// host under load can stretch a wall-clock interval.</summary>
    private static readonly TimeSpan _autoCommitIntervalBracket = TimeSpan.FromSeconds(7);

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
                // Mechanism-2 classification: does NOT need the group-clearance
                // wait — this host is built from a bare Host.CreateApplicationBuilder()
                // with AddOrdersOutbox + AddOrdersAcceptance + AddDispatcher only
                // (this test's own point, line 39-42's comment), never AddOrdersSaga,
                // so it never registers KafkaFactStreamSubscriber/SagaFactsConsumer
                // and never joins group "orders.saga" at all.
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

        // Backlog id 74 bullet 6 — a locally built host with the FULL saga
        // wiring joins the SAME literal production group, so it is wrapped
        // exactly like the ones SagaIntegrationTestSupport.StartHostAsync hands
        // out: a bare secondHost.StopAsync() here still clears the group.
        var secondHost = new KafkaGroupTestHost(secondBuilder.Build(), kafka.BootstrapServers, SagaIntegrationTestSupport.KafkaGroupId);
        await secondHost.StartAsync();
        try
        {
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "pending", _wait);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(secondHost, kafka);
        }
    }

    /// <summary>
    /// Backlog id 94 — renamed from
    /// <c>SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered</c>.
    /// Since <c>observability_reliability</c> (OR1) wrapped
    /// <c>SagaFactsConsumer</c>'s dispatch in <c>FactRetryDispatcher</c>, an
    /// ordinary handler exception is retried IN-PROCESS — caught inside
    /// <c>FactRetryDispatcher.DispatchAsync</c>'s own loop, never rethrown
    /// except for <see cref="OperationCanceledException"/> or (silently, via
    /// the DLQ path) on retry exhaustion. It therefore never reaches
    /// <c>KafkaFactStreamSubscriber.ConsumeAsync</c>'s own catch/<c>Close()</c>
    /// path, so a genuine Kafka-level redelivery of a poison message —
    /// rejoining the group and having the BROKER hand the same offset back
    /// to a fresh consumer — is no longer a reachable outcome of "a handler
    /// throws" at all; OR1 replaced it by design with bounded in-process
    /// retry + dead-letter, precisely so one poison message cannot block a
    /// partition. The old name's "AndTheFactIsRedelivered" clause is no
    /// longer true of the code it describes, so it is dropped rather than
    /// strengthened — there is no live path left under which it COULD pass.
    /// What SO9's own requirement text (§3.3, the SHALL clause) actually
    /// binds — the committed offset never advances before the handler for
    /// that message has returned successfully — is still true, is still
    /// this test's whole point, and is now the part this rename names and
    /// the "not before" half below actually proves (armed against both F6
    /// mutations; see progress/impl_id94_so9_dead_arm.md).
    /// </summary>
    [Fact]
    public async Task SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedUntilItSucceeds()
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

        // Backlog id 74 bullet 6 — a locally built host that joins the SAME
        // literal production group, wrapped exactly like the ones the test
        // support helper hands out: a bare host.StopAsync() here still clears
        // the group.
        var host = new KafkaGroupTestHost(builder.Build(), kafka.BootstrapServers, SagaIntegrationTestSupport.KafkaGroupId);
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

            // The FIRST attempt at order.placed.v1 throws inside the
            // transactional unit (EnqueueAsync) — the whole transaction
            // rolls back, so nothing is stored and OR1's FactRetryDispatcher
            // retries the SAME message in-process (backlog id 94 — this is
            // no longer a fresh Kafka delivery; see the method's own
            // doc-comment).
            var deadline = DateTime.UtcNow + _wait;
            while (DateTime.UtcNow < deadline && gate.Attempts < 1)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 1, "the decorated store was never reached — the fact never arrived at all.");

            // Wait for the in-process RETRY to arrive and reach the gate —
            // it is now provably blocked BEFORE it can touch the inner
            // store (ThrowOnceGate.BeforeEnqueueAsync), so what happens
            // next is not a race against the retry's own timing.
            var redeliveryDeadline = DateTime.UtcNow + _wait;
            while (DateTime.UtcNow < redeliveryDeadline && gate.Attempts < 2)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 2, "the in-process retry never reached the decorated store a second time within the wait budget.");

            // SO9's "not before" half, read from the broker: the retry is
            // blocked (deterministically, not by a wall-clock guess) and
            // has not been allowed to call the inner store, so nothing NEW
            // can legitimately be committed yet.
            //
            // Backlog id 94's repair — the mechanism this half used to rely
            // on is gone: OR1's FactRetryDispatcher catches the handler's
            // exception INSIDE its own retry loop and never rethrows it
            // (except OperationCanceledException), so the exception no
            // longer propagates out of KafkaFactStreamSubscriber.ConsumeAsync
            // on this path — consumer.Close() is therefore never reached
            // here, and cannot be what proves this half anymore (measured;
            // see progress/impl_id94_so9_dead_arm.md). A single read taken
            // immediately after Attempts reaches 2 cannot catch a
            // prematurely STORED offset (F6's two mutations) either: with
            // EnableAutoCommit staying true regardless, the ONLY thing that
            // turns a stored-but-not-yet-committed offset into a committed
            // one now is the periodic background committer on its own
            // auto.commit.interval.ms cadence (librdkafka default 5 000
            // ms) — so this half polls through a window that comfortably
            // exceeds one full tick of that interval WHILE the retry
            // remains deterministically blocked on the gate, and fails the
            // instant any read disagrees with the baseline, naming the
            // offset it saw.
            var noPrematureCommitDeadline = DateTime.UtcNow + _autoCommitIntervalBracket;
            long afterFailedDelivery;
            string afterFailedDescription;
            do
            {
                (afterFailedDelivery, afterFailedDescription) = await SagaIntegrationTestSupport.ReadCommittedOffsetsAsync(kafka.BootstrapServers, OrdersFactTopic.Name, SagaGroupId, OrdersFactTopicPartitionCount, TimeSpan.FromSeconds(10));
                Assert.True(baseline == afterFailedDelivery, $"the committed offset advanced to [{afterFailedDescription}] (baseline was [{baselineDescription}]) WHILE the retry was still deterministically blocked on the gate — a StoreOffset call reached the broker before the handler completed (F6: EnableAutoOffsetStore = true, or StoreOffset moved before the handler's await).");

                if (DateTime.UtcNow < noPrematureCommitDeadline)
                {
                    await Task.Delay(500);
                }
            }
            while (DateTime.UtcNow < noPrematureCommitDeadline);

            // Now let the retry proceed.
            gate.Release();

            // The SECOND attempt (the in-process retry) succeeds — a saga_commands row eventually appears.
            var sentCount = await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            Assert.True(sentCount > 0);

            // SO9's "does advance" half, read from the broker: after the
            // successful retry, StoreOffset was called, and the next
            // auto.commit.interval.ms tick commits it — polled, bounded
            // well past one interval, rather than a fixed sleep.
            var afterSuccess = await SagaIntegrationTestSupport.WaitForCommittedOffsetToExceedAsync(kafka.BootstrapServers, OrdersFactTopic.Name, SagaGroupId, OrdersFactTopicPartitionCount, baseline, TimeSpan.FromSeconds(15));
            Assert.True(afterSuccess > baseline, $"the '{SagaGroupId}' group's committed offset on '{OrdersFactTopic.Name}' never advanced past {baseline} after the successful retry (last observed {afterSuccess}) — SO9's 'only after success' half is unproven.");
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
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
        public async Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken)
        {
            await gate.BeforeEnqueueAsync(cancellationToken);
            return await inner.EnqueueAsync(orderId, orderReference, command, payload, triggeringEventId, triggeringEventEnvelope, triggeringEventTopic, cancellationToken);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => inner.TryClaimAsync(orderId, command, cancellationToken);

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => inner.ClaimDueAsync(batchSize, cancellationToken);

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => inner.MarkSentAsync(commandId, cancellationToken);

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.ParkAsync(commandId, attemptsMade, lastError, cancellationToken);

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.RejectAsync(commandId, attemptsMade, lastError, cancellationToken);

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken) => inner.TryClaimDeadLetterAsync(commandId, cancellationToken);

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken) => inner.FindOperatorCancelNoteAsync(orderId, cancellationToken);

        public Task<bool> HasAcceptedOperatorCancelAsync(Guid orderId, CancellationToken cancellationToken) => inner.HasAcceptedOperatorCancelAsync(orderId, cancellationToken);
    }
}
