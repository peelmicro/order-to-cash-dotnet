using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature <c>orders_cancel_responder</c> — operator-initiated cancellation,
/// end to end, over the REAL transport: a real NATS broker, a real MS-SQL
/// database, the real <c>orders.cancel</c> responder (the third subject on
/// <c>OrdersCreateResponder</c>), and real stand-in Fulfillment/Billing
/// responders standing in for the saga commands this feature enqueues.
/// Acceptance bullet 1 ("POST /orders/{id}/cancel succeeds through the
/// Gateway") is proven at the NATS boundary — the Gateway (id 25) does not
/// exist yet, matching <c>orders_catalog_responder</c>'s own precedent.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class OrdersCancelAcceptanceTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Placed_ThroughTheRealNatsWire_CancelsImmediatelyWithReasonOperatorCancelledAndNoCompensationPlanned()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelplaced");
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId);

            Assert.Equal(orderId, reply.OrderId);
            Assert.Equal(placed.OrderReference.Value, reply.OrderReference);
            Assert.Equal("cancelled", reply.Status);
            Assert.Equal("operator_cancelled", reply.CancellationReason);
            Assert.Empty(reply.CompensationPlanned);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("cancelled", row.Status);
            Assert.Equal("operator_cancelled", row.CancellationReason);

            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            Assert.Equal(0, payloadDoc.RootElement.GetProperty("compensationSteps").GetArrayLength());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    [Fact]
    public async Task StockReserved_EnqueuesStockReleaseOverTheRealWire_ARealStockReleasedFactCompletesTheCancellation()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelstockres");
        try
        {
            StockReleaseRequestPayload? observedRelease = null;
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                request =>
                {
                    observedRelease = request;
                    return new StockReleaseReplyPayload("released", request.OrderReference, Released: []);
                },
                CancellationToken.None);

            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url,
                request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []),
                CancellationToken.None);

            // No credit.hold responder at all — pins the order at
            // stock_reserved deterministically, isolating the SAME
            // operator-cancel-vs-saga-forward-progress race #7 disclosed
            // (see progress/impl_orders_cancel_responder.md), the same
            // technique #7's own integration suite used for the identical
            // reason.
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId);

            Assert.Equal("stock_reserved", reply.Status);
            Assert.Null(reply.CancellationReason);
            Assert.Equal(["stock_release"], reply.CompensationPlanned);

            // Not yet cancelled — the order stays stock_reserved until the
            // real stock.released.v1 fact arrives.
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            // Wait for the REAL RPC dispatch to actually reach the stand-in
            // BEFORE publishing the compensating fact — publishing it first
            // would let the order reach `cancelled` on the strength of the
            // fabricated fact alone, without ever proving the real dispatch
            // fired (review-worthy race in the test itself, not the product).
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.NotNull(observedRelease);
            Assert.Equal("order_cancelled", observedRelease!.Reason);

            var releasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, releasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: releasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("operator_cancelled", row.CancellationReason);

            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            var steps = payloadDoc.RootElement.GetProperty("compensationSteps");
            Assert.Equal(1, steps.GetArrayLength());
            Assert.Equal("stock_released", steps[0].GetProperty("step").GetString());
            Assert.Equal(releasedFactEventId, steps[0].GetProperty("eventId").GetGuid());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// The <c>credit_approved</c>/<c>confirmed</c> branch — the reverse-
    /// order-of-acquisition proof: a shared, ORDERED list two SEPARATE
    /// stand-in responders append to (never merely "both eventually
    /// happened") must read exactly <c>["credit.release", "stock.release"]</c>.
    /// No <c>despatch.create</c> responder is started at all — isolating the
    /// SAME operator-cancel-vs-saga-forward-progress race #7 disclosed,
    /// which is materially WORSE on this branch (see
    /// <c>progress/impl_orders_cancel_responder.md</c>).
    /// </summary>
    [Fact]
    public async Task CreditApprovedOrConfirmed_IssuesCreditReleaseStrictlyBeforeStockRelease_ReverseOrderOfAcquisition()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelcreditrel");
        try
        {
            var issuedOrder = new ConcurrentQueue<string>();

            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url,
                request =>
                {
                    issuedOrder.Enqueue("credit.release");
                    return new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450);
                },
                CancellationToken.None);

            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                request =>
                {
                    issuedOrder.Enqueue("stock.release");
                    return new StockReleaseReplyPayload("released", request.OrderReference, Released: []);
                },
                CancellationToken.None);

            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url,
                request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []),
                CancellationToken.None);

            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount),
                CancellationToken.None);

            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // Drives the order to confirmed by publishing the two real
            // facts the saga's own forward progress waits for — the stand-in
            // RPC responders answer the COMMAND, but only a published FACT
            // (standing in for the responder's own outbox relay) advances
            // the saga (saga.md §2's own rule).
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            // Pins the order at confirmed (despatch.create is never
            // answered) before cancelling.
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", _wait);

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId);

            Assert.Equal("confirmed", reply.Status);
            Assert.Null(reply.CancellationReason);
            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);

            // credit.release is enqueued and dispatched over the real wire
            // (the stand-in above answered it) — but only the PUBLISHED
            // credit.released.v1 FACT advances the saga, standing in for
            // Billing's own outbox relay, same discipline as every other
            // fact in this suite.
            var creditReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

            // Not yet cancelled — the order stays confirmed until
            // stock.released.v1 (the SECOND compensation step) arrives too.
            Assert.Equal("confirmed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", TimeSpan.FromSeconds(1)));

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("operator_cancelled", row.CancellationReason);

            // The ordering guarantee itself — not merely that both fired.
            Assert.Equal(["credit.release", "stock.release"], issuedOrder.ToArray());

            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            var steps = payloadDoc.RootElement.GetProperty("compensationSteps");
            Assert.Equal(2, steps.GetArrayLength());
            Assert.Equal("credit_released", steps[0].GetProperty("step").GetString());
            Assert.False(steps[0].TryGetProperty("eventId", out _), "the synthesised credit_released step carries no eventId — no cross-fact state sources the earlier fact's own id (see SagaStepTable.CompensationStepsFromCreditThenStockRelease)");
            Assert.Equal("stock_released", steps[1].GetProperty("step").GetString());
            Assert.Equal(stockReleasedFactEventId, steps[1].GetProperty("eventId").GetGuid());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    [Fact]
    public async Task Terminal_RepliesOrderNotCancellableNot503_AndLeavesTheOrderUntouched()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelterminal");
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // Drives the order straight to a terminal status via EF, purely
            // as a cheap fixture — the RPC path this test proves is the
            // responder's OWN, not how the order got there.
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                row.Status = "invoiced";
                await db.SaveChangesAsync();
            }

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var errorPayload = await CancelExpectingErrorAsync(caller, orderId);

            Assert.Equal("ORDER_NOT_CANCELLABLE", errorPayload.Code);

            await using var assertDb = mssql.CreateDbContext(connectionString);
            var unchangedRow = await assertDb.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("invoiced", unchangedRow.Status);
            Assert.Null(unchangedRow.CancellationReason);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    [Fact]
    public async Task UnknownOrderId_RepliesNotFound()
    {
        var (host, _) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelnotfound");
        try
        {
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var errorPayload = await CancelExpectingErrorAsync(caller, Guid.NewGuid());

            Assert.Equal("NOT_FOUND", errorPayload.Code);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Id 71, branch 1 of 3 (<c>stock_reserved</c>) — over the REAL wire:
    /// the note travels through <c>CancelOrderCommandHandler</c>'s synthetic
    /// envelope, into the REAL <c>saga_commands</c> row, back out through
    /// <c>SagaFactHandler.ApplyStepAsync</c> once the REAL
    /// <c>stock.released.v1</c> fact completes the cancellation, and lands
    /// on the REAL <c>order.cancelled.v1</c> outbox row — the exact bytes
    /// Kafka delivers, and (unchanged, proven separately by feature 66's
    /// Gateway end-to-end test) the exact bytes the Projector copies onto
    /// the Mongo timeline entry.
    /// </summary>
    [Fact]
    public async Task StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelstocknote");
        try
        {
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            const string note = "Buyer called to cancel before despatch.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await CancelAsync(caller, orderId, note);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);

            var releasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, releasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: releasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            // A2 — a message naming the missing note, never a bare
            // GetProperty()/KeyNotFoundException.
            Assert.True(
                payloadDoc.RootElement.TryGetProperty("note", out var noteElement),
                $"expected the order.cancelled.v1 outbox payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {payloadDoc.RootElement.GetRawText()}");
            Assert.Equal(note, noteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Id 71, branch 2 of 3 (<c>confirmed</c>) — same real chain as
    /// <see cref="CreditApprovedOrConfirmed_IssuesCreditReleaseStrictlyBeforeStockRelease_ReverseOrderOfAcquisition"/>,
    /// but through BOTH real compensation hops (<c>credit.release</c> then
    /// <c>stock.release</c>) — proving the note survives the TWO-hop chain,
    /// not merely the direct one <c>StockReserved_WithANote_…</c> covers.
    /// </summary>
    [Fact]
    public async Task Confirmed_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelconfirmednote");
        try
        {
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", _wait);

            const string note = "Retailer requested cancellation after credit was already approved.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await CancelAsync(caller, orderId, note);

            var creditReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

            // Not yet cancelled — confirms the note has not already leaked
            // through before the SECOND hop completes.
            Assert.Equal("confirmed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", TimeSpan.FromSeconds(1)));

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            // A2 — a message naming the missing note, never a bare
            // GetProperty()/KeyNotFoundException.
            Assert.True(
                payloadDoc.RootElement.TryGetProperty("note", out var noteElement),
                $"expected the order.cancelled.v1 outbox payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {payloadDoc.RootElement.GetRawText()}");
            Assert.Equal(note, noteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Id 71, branch 3 of 3 (<c>credit_approved</c>) — the ENQUEUE half
    /// specifically: <c>credit_approved</c> is not a status the natural
    /// fact-driven flow ever leaves the order resting at (<c>credit.approved.v1</c>'s
    /// own step both approves AND confirms in one transaction — see this
    /// handler's own class remarks), so this test seeds it directly via EF,
    /// the same "cheap fixture, not the path under test" precedent
    /// <see cref="Terminal_RepliesOrderNotCancellableNot503_AndLeavesTheOrderUntouched"/>
    /// already establishes. The COMPLETION half (the note surviving the
    /// two-hop chain once it is running) is identical code to
    /// <see cref="Confirmed_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes"/>
    /// and is not re-proven here.
    /// </summary>
    [Fact]
    public async Task CreditApproved_WithANote_TheEnqueuedCreditReleaseRowCarriesItInItsRealStoredEnvelope()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelcreditapprovednote");
        try
        {
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                row.Status = "credit_approved";
                await db.SaveChangesAsync();
            }

            const string note = "Cancelled directly from credit_approved.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("credit_approved", reply.Status);
            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);

            await using var db2 = mssql.CreateDbContext(connectionString);
            var row2 = await db2.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == "credit.release");
            Assert.NotNull(row2.TriggeringEventEnvelope);
            using var envelopeDoc = JsonDocument.Parse(row2.TriggeringEventEnvelope!);
            Assert.Equal("orders.cancel.requested", envelopeDoc.RootElement.GetProperty("eventType").GetString());
            var envelopePayload = envelopeDoc.RootElement.GetProperty("payload");
            // A2 — a message naming the missing note, never a bare
            // GetProperty()/KeyNotFoundException.
            Assert.True(
                envelopePayload.TryGetProperty("note", out var envelopeNoteElement),
                $"expected the stored credit.release envelope's payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {envelopePayload.GetRawText()}");
            Assert.Equal(note, envelopeNoteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Id 71's parity bullet — a parked operator-cancel compensation command
    /// (deliberately NO <c>stock.release</c> responder, so SO4 exhausts and
    /// SO5 parks it) publishes the SAME synthetic envelope R29's dead-letter
    /// clause already threads through <see cref="SagaFirstParkDeadLetterHandler"/>
    /// to <c>.dlq</c>, byte-equal to the stored column — porting #7's
    /// non-nullable port (<c>saga-command-store.port.ts:40</c>): an
    /// operator-cancel compensation now dead-letters SOMETHING, closing the
    /// A2 parity gap feature 27's review disclosed. Armed by restoring the
    /// <c>null</c> at the enqueue site (record's arming table).
    /// </summary>
    [Fact]
    public async Task StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope()
    {
        const string SourceTopic = OrdersFactTopic.Name;
        const string DlqTopic = SourceTopic + ".dlq";
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = DlqTopic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }

        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "cancelstockparks");
        try
        {
            // Deliberately NO stock.release responder — the compensation
            // command exhausts SO4's in-line retries and parks (SO5).
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            const string note = "Parked compensation — no stock.release responder.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await CancelAsync(caller, orderId, note);

            // Poll for the row to actually park AND be dead-lettered — the
            // same "poll the condition the assertion depends on" discipline
            // SagaCommandDeadLetterTests uses, never "Status == parked" alone.
            SagaCommand? parkedRow = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await using var pollDb = mssql.CreateDbContext(connectionString);
                parkedRow = await pollDb.SagaCommands.AsNoTracking().SingleOrDefaultAsync(
                    c => c.OrderId == orderId && c.Command == "stock.release" && c.Status == "parked" && c.DeadLetteredAt != null);
                if (parkedRow is not null)
                {
                    break;
                }

                await Task.Delay(150);
            }

            Assert.NotNull(parkedRow);
            Assert.NotNull(parkedRow!.TriggeringEventEnvelope);
            var storedEnvelopeBytes = System.Text.Encoding.UTF8.GetBytes(parkedRow.TriggeringEventEnvelope!);

            var dlqMessage = await ConsumeMatchingAsync(DlqTopic, orderId, TimeSpan.FromSeconds(30));
            Assert.NotNull(dlqMessage);
            Assert.Equal(storedEnvelopeBytes, dlqMessage!.Message.Value);

            using var dlqDoc = JsonDocument.Parse(dlqMessage.Message.Value);
            Assert.Equal("orders.cancel.requested", dlqDoc.RootElement.GetProperty("eventType").GetString());
            var dlqPayload = dlqDoc.RootElement.GetProperty("payload");
            // A2 — a message naming the missing note, never a bare
            // GetProperty()/KeyNotFoundException.
            Assert.True(
                dlqPayload.TryGetProperty("note", out var dlqNoteElement),
                $"expected the DLQ copy's payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {dlqPayload.GetRawText()}");
            Assert.Equal(note, dlqNoteElement.GetString());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>Same shape as <c>SagaCommandDeadLetterTests.ConsumeMatchingAsync</c> — reads from <see cref="Offset.Beginning"/> over the topic's own known partitions and returns the FIRST message whose envelope <c>correlationId</c> equals <paramref name="orderId"/>.</summary>
    private async Task<ConsumeResult<string, byte[]>?> ConsumeMatchingAsync(string topic, Guid orderId, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"operator-cancel-dlq-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6)
            .Select(p => new TopicPartitionOffset(topic, new Partition(p), Offset.Beginning))
            .ToList());

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null || result.IsPartitionEOF)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(result.Message.Value);
                if (document.RootElement.TryGetProperty("correlationId", out var correlationId) && correlationId.GetGuid() == orderId)
                {
                    return result;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static Task<OrdersCancelReplyPayload> CancelAsync(INatsConnection caller, Guid orderId) => CancelAsync(caller, orderId, note: "integration test");

    private static async Task<OrdersCancelReplyPayload> CancelAsync(INatsConnection caller, Guid orderId, string? note)
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

    private static async Task<RpcErrorPayload> CancelExpectingErrorAsync(INatsConnection caller, Guid orderId)
    {
        var replyMsg = await caller.RequestAsync<byte[], byte[]>(
            RpcSubjects.OrdersCancel,
            RpcJson.Serialize(new OrdersCancelRequestPayload(orderId, OrderReference: null, "operator_cancelled", Note: null)),
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
            cancellationToken: CancellationToken.None);

        Assert.NotNull(replyMsg.Data);
        Assert.True(RpcJson.IsErrorBody(replyMsg.Data!), $"expected an error body, got: {System.Text.Encoding.UTF8.GetString(replyMsg.Data!)}");

        return RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);
    }
}
