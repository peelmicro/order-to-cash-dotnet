using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 62 — the race <c>orders_cancel_responder</c>'s own remarks
/// disclosed (<see cref="OrderToCash.Orders.Application.Commands.CancelOrderCommandHandler"/>'s
/// class doc) and #7's <c>orders-cancel.integration.spec.ts</c> reproduced
/// live and never fixed (Finding 1,
/// <c>progress/review_orders_catalog_and_cancel_responders.md</c> in the
/// #7 checkout). Both isolations <c>OrdersCancelAcceptanceTests</c>
/// deliberately builds in (no <c>credit.hold</c> responder pins
/// <c>stock_reserved</c>; no <c>despatch.create</c> responder pins
/// <c>confirmed</c>) are REMOVED here: this suite starts every responder
/// the natural chain needs and drives the SECOND, competing fact in
/// explicitly, by hand, at the exact moment each test chooses.
///
/// Two branches, two orderings each (CLAUDE.md's two-party rule): "saga
/// forward progress first" is the bug (a genuinely-released resource
/// stranded on a progressing order); "operator decision first" is the
/// control proving R25 keeps correctly ignoring a fact whose precondition
/// genuinely no longer holds (bullet 2) — the SAME infrastructure, the
/// SAME assertions shape, only the publish order swapped.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class OperatorCancelRacesSagaForwardProgressTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Branch A (<c>stock_reserved</c>), "saga forward progress first" —
    /// the bug. Today's code (id 62's fix REMOVED — see this test's own
    /// arming entry in <c>progress/impl_operator_cancel_races_saga_forward_progress.md</c>)
    /// lets <c>credit.approved.v1</c> advance the order to <c>confirmed</c>
    /// even though the operator's own <c>stock.release</c> compensation is
    /// already enqueued; when <c>stock.released.v1</c> — a REAL, genuine
    /// release — finally arrives, R25 finds the order at <c>confirmed</c>,
    /// not <c>stock_reserved</c>, and correctly-but-harmfully ignores it:
    /// the stranded end state is an order stuck at <c>confirmed</c> with
    /// <c>cancellation_reason: NULL</c> and a released stock reservation
    /// the order record never reflects. Fixed, this test instead proves
    /// <c>credit.approved.v1</c> is SUPERSEDED (never applied) and the
    /// compensation completes normally from <c>stock_reserved</c>.
    /// </summary>
    [Fact]
    public async Task BranchA_StockReserved_SagaForwardProgressFirst_CreditApprovedIsSupersededAndStockReleaseStillCompletesTheCancellation()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceA1");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            const string note = "Buyer called to cancel — while credit approval was still racing in the background.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("stock_reserved", reply.Status);
            Assert.Equal(["stock_release"], reply.CompensationPlanned);

            // Forward progress — a credit.approved.v1 fact for THIS order,
            // exactly as the normal happy path would eventually deliver
            // (the credit.hold RPC the saga dispatched on reaching
            // stock_reserved is genuinely independent of the cancel this
            // test just issued). Published BEFORE the compensating
            // stock.released.v1 fact — "saga forward progress first".
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            var supersededCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "credit.approved.v1", "superseded", _wait);
            Assert.True(supersededCount > 0, $"expected a 'superseded' saga_ignored_facts row for credit.approved.v1 on order {orderId} — none appeared within {_wait}. If this fails, the forward-progress fact was allowed to advance the order instead.");

            // Never advanced — the exact bug this test reproduces on
            // unfixed code would show "confirmed" here instead.
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            // Bullet 2 — exactly ONE superseded row, never a retry storm:
            // the idempotent runner's own dedup (by eventId) means an
            // ignored fact is recorded once, not reprocessed.
            await using (var countDb = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(1, await countDb.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.EventType == "credit.approved.v1" && f.Marker == "superseded"));
            }

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            var releasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, releasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: releasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("operator_cancelled", row.CancellationReason);

            // Bullet 4, under THIS race: the note that reaches
            // order.cancelled.v1 is the operator's own.
            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            Assert.True(payloadDoc.RootElement.TryGetProperty("note", out var noteElement), $"expected order.cancelled.v1 to carry \"note\": \"{note}\", payload: {payloadDoc.RootElement.GetRawText()}");
            Assert.Equal(note, noteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch A, "operator decision first" — the control (bullet 2). The
    /// compensation completes FIRST; <c>credit.approved.v1</c> arrives only
    /// AFTER the order is already <c>cancelled</c>, so its precondition
    /// (<c>stock_reserved</c>) genuinely no longer holds — R25's ORIGINAL,
    /// unmodified <c>precondition_unmet</c> path, not the new superseded
    /// one, and the order is left untouched. Unaffected by id 62's fix —
    /// passes identically with or without it, proving the fix does not
    /// turn this correct no-op into anything else.
    /// </summary>
    [Fact]
    public async Task BranchA_StockReserved_OperatorDecisionFirst_CreditApprovedArrivingAfterCancellationIsIgnoredByPreconditionUnmet()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceA2");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, "cancel wins the race this time");
            Assert.Equal("stock_reserved", reply.Status);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            var releasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, releasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: releasedFactEventId);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            // LATE forward progress — arrives only after cancellation.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            var ignoredCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "credit.approved.v1", "precondition_unmet", _wait);
            Assert.True(ignoredCount > 0, $"expected a 'precondition_unmet' saga_ignored_facts row for credit.approved.v1 on order {orderId} — none appeared within {_wait}.");

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("cancelled", row.Status); // unchanged by the late fact.
            Assert.Equal("operator_cancelled", row.CancellationReason);

            // Bullet 2 — exactly ONE precondition_unmet row, no retry storm.
            Assert.Equal(1, await db.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.EventType == "credit.approved.v1" && f.Marker == "precondition_unmet"));
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch B (<c>confirmed</c>), "saga forward progress first" — #7's
    /// literal Finding 1, reproduced: <c>order.despatched.v1</c> (the
    /// <c>despatch.create</c> command the saga's OWN forward progress
    /// already dispatched on reaching <c>confirmed</c>, before the operator
    /// ever cancelled) arrives and, on unfixed code, advances the order to
    /// <c>despatched</c> even though the operator's two-hop compensation
    /// (<c>credit.release</c> then <c>stock.release</c>) is already
    /// enqueued — stranding it there with <c>cancellation_reason: NULL</c>.
    /// Fixed, <c>order.despatched.v1</c> is SUPERSEDED and the compensation
    /// chain completes normally from <c>confirmed</c>.
    /// </summary>
    [Fact]
    public async Task BranchB_Confirmed_SagaForwardProgressFirst_DespatchedIsSupersededAndTheCompensationChainStillCompletesTheCancellation()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceB1");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", _wait);

            const string note = "Retailer requested cancellation just as despatch.create was already in flight.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("confirmed", reply.Status);
            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);

            // Forward progress — #7's literal mechanism: the despatch.create
            // command the saga dispatched BEFORE the cancel was ever
            // requested now replies, and its fact arrives BEFORE either
            // compensation hop completes.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "order.despatched.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new OrderDespatchedPayload(reference, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, []), CancellationToken.None);

            var supersededCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "order.despatched.v1", "superseded", _wait);
            Assert.True(supersededCount > 0, $"expected a 'superseded' saga_ignored_facts row for order.despatched.v1 on order {orderId} — none appeared within {_wait}. If this fails, the forward-progress fact was allowed to advance the order to despatched instead — #7's own Finding 1.");

            // Never advanced — the exact stranding this test reproduces on
            // unfixed code would show "despatched" here instead.
            Assert.Equal("confirmed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", TimeSpan.FromSeconds(1)));

            // Bullet 2 — exactly ONE superseded row, no retry storm.
            await using (var countDb = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(1, await countDb.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.EventType == "order.despatched.v1" && f.Marker == "superseded"));
            }

            var creditReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

            Assert.Equal("confirmed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", TimeSpan.FromSeconds(1)));

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("operator_cancelled", row.CancellationReason);

            // Bullet 4, under THIS race, two-hop shape.
            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            Assert.True(payloadDoc.RootElement.TryGetProperty("note", out var noteElement), $"expected order.cancelled.v1 to carry \"note\": \"{note}\", payload: {payloadDoc.RootElement.GetRawText()}");
            Assert.Equal(note, noteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch B, "operator decision first" — the control (bullet 2). The
    /// two-hop compensation completes FIRST; <c>order.despatched.v1</c>
    /// arrives only AFTER the order is <c>cancelled</c>, so R25's ORIGINAL
    /// <c>precondition_unmet</c> path fires, unaffected by id 62's fix.
    /// </summary>
    [Fact]
    public async Task BranchB_Confirmed_OperatorDecisionFirst_DespatchedArrivingAfterCancellationIsIgnoredByPreconditionUnmet()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceB2");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", _wait);

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, "compensation wins the race this time");
            Assert.Equal("confirmed", reply.Status);

            var creditReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            // LATE forward progress — arrives only after cancellation.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "order.despatched.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new OrderDespatchedPayload(reference, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, []), CancellationToken.None);

            var ignoredCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "order.despatched.v1", "precondition_unmet", _wait);
            Assert.True(ignoredCount > 0, $"expected a 'precondition_unmet' saga_ignored_facts row for order.despatched.v1 on order {orderId} — none appeared within {_wait}.");

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("cancelled", row.Status); // unchanged by the late fact.
            Assert.Equal("operator_cancelled", row.CancellationReason);

            // Bullet 2 — exactly ONE precondition_unmet row, no retry storm.
            Assert.Equal(1, await db.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.EventType == "order.despatched.v1" && f.Marker == "precondition_unmet"));
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    private static Task<OrdersCancelReplyPayload> CancelAsync(INatsConnection caller, Guid orderId, string? note) => CancelInternalAsync(caller, orderId, note);

    private static async Task<OrdersCancelReplyPayload> CancelInternalAsync(INatsConnection caller, Guid orderId, string? note)
    {
        var replyMsg = await caller.RequestAsync<byte[], byte[]>(
            RpcSubjects.OrdersCancel,
            RpcJson.Serialize(new OrdersCancelRequestPayload(orderId, OrderReference: null, "operator_cancelled", Note: note)),
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
            cancellationToken: CancellationToken.None);

        Assert.NotNull(replyMsg.Data);
        Assert.False(RpcJson.IsErrorBody(replyMsg.Data!), $"expected a success reply, got an error body: {System.Text.Encoding.UTF8.GetString(replyMsg.Data!)}");

        return RpcJson.Deserialize<OrdersCancelReplyPayload>(replyMsg.Data!);
    }
}
