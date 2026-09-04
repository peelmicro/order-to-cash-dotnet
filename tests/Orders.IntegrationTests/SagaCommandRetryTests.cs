using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// design.md §8.1 — R29's retry clause, SO3 (the crash-window composition),
/// SO4 (in-line retry/backoff) and SO5 (park + sweeper resume): with no
/// responder, a <c>parked</c> row appears with attempts and error while the
/// order status is unchanged; a <c>pending</c> row committed with NO
/// in-process signal — simulating a crash between commit and the hop — is
/// still issued by a sweeper cycle, and resumes the saga once a responder
/// appears.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaCommandRetryTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task R29_SO4_SO5_WithNoResponder_ParksAfterExhaustedAttemptsLeavingTheOrderStatusUnchanged()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "retrypark");
        try
        {
            // Deliberately NO stand-in for fulfillment.stock.reserve.
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await using var db = mssql.CreateDbContext(connectionString);
            var deadline = DateTime.UtcNow + _wait;
            SagaCommand? parkedRow = null;

            while (DateTime.UtcNow < deadline)
            {
                parkedRow = await db.SagaCommands.AsNoTracking().SingleOrDefaultAsync(c => c.OrderId == orderId && c.Status == "parked");
                if (parkedRow is not null)
                {
                    break;
                }

                await Task.Delay(200);
            }

            Assert.NotNull(parkedRow);
            Assert.Equal("stock.reserve", parkedRow!.Command);
            Assert.Equal(3, parkedRow.Attempts);
            Assert.NotNull(parkedRow.LastError);
            Assert.NotNull(parkedRow.NextAttemptAt);

            // R29 — the order's status is NEVER touched while retrying/parked.
            Assert.Equal("placed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "placed", TimeSpan.FromSeconds(1)));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task SO3_APendingRowCommittedWithNoInProcessSignal_IsStillIssuedBySweeperCycleAndResumesTheSagaWhenAResponderAppears()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "crashwindow");
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(nats.Url, r => new StockReserveReplyPayload("accepted", r.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(nats.Url, r => new CreditHoldReplyPayload("approved", r.OrderReference, r.Amount.Currency, 5_000_00, HeldAmount: r.Amount.Amount), CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // Drive to stock_reserved through the normal path first — this
            // naturally enqueues and sends credit.hold via the live signal,
            // which is NOT the row under test here.
            await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopicsFulfillment, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, new OrderToCash.Contracts.Facts.Payloads.StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);

            // Now simulate the SO3 crash window directly: a `pending`
            // saga_commands row for `stock.release` — a command the current
            // flow never naturally owes at `stock_reserved` (only
            // `credit.rejected.v1` would ask for it), so it cannot collide
            // with anything the real saga enqueues — committed WITHOUT ever
            // going through SagaFactHandler/ChannelSagaCommandSignal. No
            // in-process signal was ever published for it; only a sweeper
            // cycle can resume it.
            var payload = Encoding.UTF8.GetString(RpcJson.Serialize(new StockReleaseRequestPayload(placed.OrderReference.Value, "credit_rejected")));
            var now = DateTime.UtcNow;
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                db.SagaCommands.Add(new SagaCommand
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    OrderReference = placed.OrderReference.Value,
                    Command = "stock.release",
                    Payload = payload,
                    TriggeringEventId = Guid.NewGuid(),
                    Status = "pending",
                    Attempts = 0,
                    CreatedAt = now.AddSeconds(-2), // already past PendingGraceMs (300ms) by the time the sweeper next runs.
                    UpdatedAt = now.AddSeconds(-2),
                });
                await db.SaveChangesAsync();
            }

            // No responder for stock.release yet — confirm the row is still
            // there (untouched by the live signal, since none was ever
            // published for it) before starting the stand-in.
            await Task.Delay(700); // at least one sweep interval (500ms).
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == "stock.release");
                Assert.True(row.Status is "pending" or "parked");
            }

            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                r => new StockReleaseReplyPayload("released", r.OrderReference, Released: []),
                CancellationToken.None);

            // The next sweep resumes it — the stock.release command, never
            // signalled in-process, still gets issued and marked sent.
            var sentCount = await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.True(sentCount > 0);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private const string SagaFactTopicsFulfillment = "otc.fulfillment.facts.v1";

    [Fact]
    public async Task SO3_DisablingTheSweeper_LeavesTheCrashWindowRowUnresolved()
    {
        // ⚑ ARM evidence, recorded verbatim in progress/impl_order_saga_orchestrator.md —
        // disabling the sweeper (Sweeper.Enabled = false) must make the SO3
        // crash-window case above fail; this test proves the negative
        // directly rather than by inspection.
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "crashwindow_disabled", options => options.Sweeper.Enabled = false);
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            var payload = Encoding.UTF8.GetString(RpcJson.Serialize(new CreditHoldRequestPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, new SagaMoney(2_450, "EUR"))));
            var now = DateTime.UtcNow;
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                db.SagaCommands.Add(new SagaCommand
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    OrderReference = placed.OrderReference.Value,
                    Command = "credit.hold",
                    Payload = payload,
                    TriggeringEventId = Guid.NewGuid(),
                    Status = "pending",
                    Attempts = 0,
                    CreatedAt = now.AddSeconds(-2),
                    UpdatedAt = now.AddSeconds(-2),
                });
                await db.SaveChangesAsync();
            }

            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                r => new CreditHoldReplyPayload("approved", r.OrderReference, r.Amount.Currency, 5_000_00, HeldAmount: r.Amount.Amount),
                CancellationToken.None);

            // With the sweeper disabled, and no in-process signal ever
            // published for this row, nothing ever claims it.
            await Task.Delay(2_000);

            await using var assertDb = mssql.CreateDbContext(connectionString);
            var stillPending = await assertDb.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == "credit.hold");
            Assert.Equal("pending", stillPending.Status);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// design.md §5.1, §5.2, SO3's own "closes the crash window" argument —
    /// commit-before-issue is atomic: the aggregate's status change, the
    /// dedup record, the outbox rows AND the pending-command row are ONE
    /// transaction. Proven the way that argument actually depends on: force
    /// <see cref="ISagaCommandStore.EnqueueAsync"/> to fail INSIDE that same
    /// transaction and assert the STATUS CHANGE rolls back with it — not
    /// just that the command row is missing, but that nothing else committed
    /// either. tasks.md L7.2's "if none does, add one": no existing test
    /// distinguished "enqueue inside the transaction" from "enqueue after
    /// commit" until this one.
    /// </summary>
    [Fact]
    public async Task SO3_CommitBeforeIssue_WhenEnqueueFailsInsideTheTransactionTheAggregateChangeRollsBackToo()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_saga_commitbeforeissue_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

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

        builder.Services.Replace(ServiceDescriptor.Scoped<ISagaCommandStore>(sp =>
            new ThrowingOnCreditHoldSagaCommandStore(new OrderToCash.Orders.Infrastructure.Saga.EfCoreSagaCommandStore(
                sp.GetRequiredService<OrderToCash.Orders.Infrastructure.Persistence.OrdersDbContext>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrderToCash.Orders.Infrastructure.OrdersSagaOptions>>()))));

        var host = builder.Build();
        await host.StartAsync();
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(nats.Url, r => new StockReserveReplyPayload("accepted", r.OrderReference, Reservations: []), CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);

            // stock.reserved.v1 owns credit.hold, and the decorated store
            // throws on THAT enqueue — inside the same transaction as
            // MarkStockReserved, if commit-before-issue holds.
            await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, "otc.fulfillment.facts.v1", "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, new OrderToCash.Contracts.Facts.Payloads.StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);

            // Give the (failing) processing attempt time to run and fail.
            await Task.Delay(2_000);

            await using var db = mssql.CreateDbContext(connectionString);
            var order = await db.Orders.SingleAsync(o => o.Id == orderId);

            // If commit-before-issue holds: the failed enqueue rolled back
            // the WHOLE transaction, so the status is still "placed" and no
            // credit.hold row exists at all — a later, successful redelivery
            // (not exercised here) would still be able to reprocess it. This
            // is what fails when the enqueue moves outside the transaction.
            Assert.Equal("placed", order.Status);
            Assert.Equal(0, await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "credit.hold"));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class ThrowingOnCreditHoldSagaCommandStore(ISagaCommandStore inner) : ISagaCommandStore
    {
        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken)
        {
            if (command == SagaCommandKind.CreditHold)
            {
                throw new InvalidOperationException("SO3 atomicity test seam: simulated enqueue failure.");
            }

            return inner.EnqueueAsync(orderId, orderReference, command, payload, triggeringEventId, cancellationToken);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => inner.TryClaimAsync(orderId, command, cancellationToken);

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => inner.ClaimDueAsync(batchSize, cancellationToken);

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => inner.MarkSentAsync(commandId, cancellationToken);

        public Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.ParkAsync(commandId, attemptsMade, lastError, cancellationToken);

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => inner.RejectAsync(commandId, attemptsMade, lastError, cancellationToken);
    }
}
