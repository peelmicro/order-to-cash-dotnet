using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 62 — rework pass 2, SA-4 (the human-gated shared-spec
/// amendment ruled 2026-09-11; <c>specs/shared/saga.md</c> §4.3 "The
/// despatch already requested" and "A credit approval that arrives after
/// the cancellation"). Both branches, both orderings each: the
/// <c>confirmed</c> branch races a REAL arbitrating stand-in Fulfillment
/// (<see cref="RecordingFulfillmentStandIn"/>) — a <c>despatch.create</c>
/// already in flight against a fresh <c>stock.release</c>, one lock, one
/// winner; the <c>stock_reserved</c> branch races a late
/// <c>credit.approved.v1</c> for a hold issued before the cancellation
/// against the compensation's own <c>stock.released.v1</c>. Every
/// interleave is forced by an explicit, test-controlled gate — never by
/// repetition or wall-clock luck.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class OperatorCancelRacesSagaForwardProgressTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Branch B (<c>confirmed</c>), "release wins" — SA-4's own release-first
    /// arbitration: the operator's <c>stock.release</c> reaches Fulfillment's
    /// one lock BEFORE the despatch already requested (forced by
    /// <see cref="RecordingFulfillmentStandIn.HoldDespatchCreateGate"/>, held
    /// closed until <c>stock.release</c> is confirmed <c>sent</c> — never a
    /// race left to chance). The despatch is refused <c>PRECONDITION_FAILED</c>
    /// and resolved as a TERMINAL rejection on its FIRST attempt — no park,
    /// no dead-letter, no <c>order.saga_failed.v1</c> — then credit is
    /// released and the order is cancelled <c>operator_cancelled</c>.
    /// </summary>
    [Fact]
    public async Task Confirmed_ReleaseWins()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(
            mssql, kafka, nats, "raceReleaseWins",
            // A generous per-attempt budget — the despatch.create RPC is
            // held open by the stand-in's own gate (below) until stock.release
            // is confirmed sent; a short budget would let the FIRST attempt
            // time out and retry/park before the gate ever opens, which
            // would dead-letter a row this test asserts never dead-letters.
            //
            // A3 (review round 1) — the margin, stated: cancelling and
            // stock.release reaching "sent" must both complete inside this
            // 10 000 ms window, comfortably inside it in practice. Before id
            // 80's fix, stock.release could only be delivered by the
            // sweeper here (Sweeper.IntervalMs = 500, SagaIntegrationTestSupport.cs:58)
            // — roughly a 20x margin over its polling interval; id 80's fix
            // now delivers it directly over the fast path's own parallel
            // dispatch, comfortably inside the SAME window by an even wider
            // margin. Not a sleep standing in for an ordering either way. If
            // it does not complete in time, a retried despatch.create queues
            // behind the gate and fulfillment.CommandsProcessed gains a
            // second entry, which the assertions below would catch, not
            // silently pass.
            configureSaga: options => options.Command.TimeoutMs = 10_000);
        try
        {
            await using var fulfillment = await RecordingFulfillmentStandIn.StartAsync(nats.Url, CancellationToken.None);
            var creditReleaseCalls = new ConcurrentQueue<string>();
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url,
                request =>
                {
                    creditReleaseCalls.Enqueue(request.OrderReference);
                    return new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450);
                },
                CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            // The despatch already requested — held closed until stock.release
            // is confirmed sent, below (SA-4's own scenario name in saga.md).
            //
            // Backlog id 62 fix round 2, G2 originally recorded that THIS
            // test depended on SagaCommandSweeper to dispatch stock.release,
            // because the (then single-reader) fast-path worker was blocked
            // on the gated despatch.create RPC and could not also carry the
            // cancel's own stock.release signal — filed as its own backlog
            // entry (id 80) rather than fixed there. Id 80 fixed it:
            // SagaCommandDispatchWorker now runs OrdersSagaDispatchOptions.DegreeOfParallelism
            // parallel consumer loops over the SAME channel
            // (ChannelSagaCommandSignal.cs, SingleReader = false), so
            // stock.release's own signal is picked up by a DIFFERENT loop
            // and dispatched directly — the fast path itself now delivers
            // it, independent of the sweeper's schedule. This test's own
            // assertions never depended on WHICH mechanism delivered it
            // (WaitForSagaCommandCountAsync below only waits for "sent"),
            // so they are unchanged; only this comment's explanation was
            // stale and is corrected here.
            fulfillment.HoldDespatchCreateGate.Reset();

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

            // The despatch.create RPC the saga's own forward progress just
            // dispatched is now in flight, blocked on the stand-in's gate —
            // it will not be answered until this test opens it, below.

            const string note = "Retailer requested cancellation just as despatch.create was already in flight.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("confirmed", reply.Status);
            Assert.Equal(["stock_release", "credit_release"], reply.CompensationPlanned);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.Equal(["stock.release"], fulfillment.CommandsProcessed);

            // NOW let the blocked despatch.create request through — it
            // observes the reservation already released under the SAME
            // stand-in lock and is refused.
            fulfillment.HoldDespatchCreateGate.Open();

            // F3 — waits for EITHER terminal resolution (the row leaving
            // `pending`), never "rejected" specifically: a mutation routing
            // the terminal rejection through the park path (record's
            // arming table, F3) leaves this row "parked" and
            // dead-lettered, never "rejected" — a wait keyed to "rejected"
            // alone would simply time out and never let the DeadLetteredAt
            // claim below be the one that fails.
            await WaitForDespatchCreateToLeavePendingAsync(connectionString, orderId, _wait);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var despatchRow = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == "despatch.create");
                // DeadLetteredAt asserted FIRST, deliberately, and BEFORE
                // fulfillment.CommandsProcessed below: under the F3
                // mutation the sweeper keeps re-claiming the parked row
                // (SO5's own backstop) and would otherwise accumulate
                // further despatch.create attempts before this claim is
                // ever reached — DeadLetteredAt itself is set on the FIRST
                // park, so checking it immediately after the row first
                // leaves `pending` is what makes THIS the claim that fails.
                Assert.Null(despatchRow.DeadLetteredAt);
                Assert.Equal("rejected", despatchRow.Status);
            }

            Assert.Equal(["stock.release", "despatch.create"], fulfillment.CommandsProcessed);

            var stockReleasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            var creditReleasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("operator_cancelled", row.CancellationReason);

                var cancelledCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                Assert.Equal(1, cancelledCount);
                var sagaFailedCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.saga_failed.v1");
                Assert.Equal(0, sagaFailedCount);

                // D2 (review round 1) — this is the interleave where a
                // credit.release row carrying a REAL fact's own envelope
                // (stock.released.v1, above) exists when the cancellation
                // completes, so content-versus-position selection in
                // FindOperatorCancelNoteAsync decides the outcome: the
                // operator's own note must still reach order.cancelled.v1,
                // never a null or a saga-decided row's (there is none here,
                // but a selection-by-command mutation would still return
                // something other than the operator's text).
                var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                using var cancelledPayload = JsonDocument.Parse(cancelledRow.Payload);
                Assert.True(
                    cancelledPayload.RootElement.TryGetProperty("note", out var cancelledNoteElement),
                    $"expected the order.cancelled.v1 outbox payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {cancelledPayload.RootElement.GetRawText()}");
                Assert.Equal(note, cancelledNoteElement.GetString());
            }

            Assert.Equal([reference], creditReleaseCalls.ToArray());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch B (<c>confirmed</c>), "despatch wins" — #7's literal
    /// mechanism, the outcome SA-4 accepts rather than closes: the
    /// despatch already requested reaches Fulfillment's lock and consumes
    /// the reservation BEFORE the operator's cancel is even issued (forced
    /// deterministically by waiting for <c>despatch.create</c> to be
    /// confirmed <c>sent</c> BEFORE calling cancel — the order's own status
    /// is still <c>confirmed</c> at that point, since only the FACT, never
    /// the RPC reply, advances it). <c>stock.release</c> then releases
    /// nothing (<c>already_released</c>, no fact); NO <c>credit.release</c>
    /// is ever issued; <c>order.despatched.v1</c> advances the order; no
    /// <c>order.cancelled.v1</c> is ever emitted — the cancellation is
    /// overtaken.
    /// </summary>
    [Fact]
    public async Task Confirmed_DespatchWins()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceDespatchWins");
        try
        {
            await using var fulfillment = await RecordingFulfillmentStandIn.StartAsync(nats.Url, CancellationToken.None);
            var creditReleaseCalls = new ConcurrentQueue<string>();
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url,
                request =>
                {
                    creditReleaseCalls.Enqueue(request.OrderReference);
                    return new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450);
                },
                CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var invoiceIssue = await StandInSagaResponders.StartInvoiceIssueAsync(
                nats.Url, request => new InvoiceIssueReplyPayload(request.OrderReference, "INV-000001", DateTimeOffset.UtcNow, request.Currency, 2_450, "issued", true), CancellationToken.None);
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

            // Deterministic "despatch wins": the saga's own despatch.create
            // dispatch is confirmed COMPLETED (the reservation consumed
            // under the stand-in's lock) BEFORE the operator ever cancels —
            // never a race, a controlled sequencing of this test's own two
            // independent actions.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "despatch.create", "sent", _wait);
            Assert.Equal(["despatch.create"], fulfillment.CommandsProcessed);

            // The order's OWN status is still confirmed — only the FACT,
            // never the RPC reply, advances it (saga.md §2).
            Assert.Equal("confirmed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", TimeSpan.FromSeconds(1)));

            const string note = "Retailer requested cancellation — but despatch had already gone out.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("confirmed", reply.Status);
            Assert.Equal(["stock_release", "credit_release"], reply.CompensationPlanned);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.Equal(["despatch.create", "stock.release"], fulfillment.CommandsProcessed);

            // A4 (review round 1) — made observable, not merely asserted by
            // absence: despatch.create already consumed the reservation, so
            // this stock.release call's own reply outcome is
            // already_released, not released.
            Assert.Equal(["already_released"], fulfillment.StockReleaseOutcomes);

            // stock.release released NOTHING — the stand-in mirrors
            // OrderStockReservation.Release's own AlreadyReleased no-op — so
            // NO stock.released.v1 fact is ever published (no code path in
            // this test publishes one), and the saga therefore never owes
            // credit.release.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "order.despatched.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new OrderDespatchedPayload(reference, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, []), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "despatched", _wait);

            // Give the (never-issued) credit.release every chance to have
            // shown up if it were going to — none exists.
            await Task.Delay(TimeSpan.FromSeconds(1));

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("despatched", row.Status);
                Assert.Null(row.CancellationReason);

                var creditReleaseRowCount = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "credit.release");
                Assert.Equal(0, creditReleaseRowCount);

                var cancelledCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                Assert.Equal(0, cancelledCount);
            }

            Assert.Empty(creditReleaseCalls);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch A (<c>stock_reserved</c>), ordering 1 — the hold's own
    /// approval arrives AFTER the operator's compensation has already
    /// completed the cancellation (order already <c>cancelled</c>, reason
    /// <c>operator_cancelled</c>). SA-4's own new clause: the late
    /// <c>credit.approved.v1</c> issues <c>credit.release</c> and NOTHING
    /// else — no transition, no error, no second cancel — so the hold does
    /// not strand.
    /// </summary>
    [Fact]
    public async Task StockReserved_LateApproval_AfterStockReleased()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceLateAfter");
        try
        {
            var creditHoldCalls = new ConcurrentQueue<string>();
            var creditReleaseCalls = new ConcurrentQueue<string>();
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                request =>
                {
                    creditHoldCalls.Enqueue(request.OrderReference);
                    return new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount);
                },
                CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url,
                request =>
                {
                    creditReleaseCalls.Enqueue(request.OrderReference);
                    return new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450);
                },
                CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            // The hold IS acquired — a real credit.hold RPC, answered
            // "approved" — but its OWN fact is deliberately never
            // published yet: this test publishes credit.approved.v1 itself,
            // LATE, below.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            Assert.Equal([reference], creditHoldCalls.ToArray());

            const string note = "Buyer cancelled while credit approval was still in flight.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("stock_reserved", reply.Status);
            Assert.Equal(["stock_release"], reply.CompensationPlanned);

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            // NOW the late credit.approved.v1 arrives, for an order already cancelled.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);

            // Give a second (wrong) credit.release, a transition, or a
            // second ignored (precondition_unmet) record every chance to
            // have shown up. F5 — "superseded" no longer exists as a
            // possible outcome at all (SA-4 retired the marker with its
            // only producer); this comment used to name it as one of the
            // wrong outcomes being ruled out and is corrected here.
            await Task.Delay(TimeSpan.FromSeconds(1));

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("cancelled", row.Status); // unchanged by the late fact.
                Assert.Equal("operator_cancelled", row.CancellationReason);

                var creditReleaseRowCount = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "credit.release");
                Assert.Equal(1, creditReleaseRowCount);

                var cancelledCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                Assert.Equal(1, cancelledCount); // never a second cancellation.

                // F4 — the late fact must never take the ORDINARY Advance
                // (approve, confirm, dispatch despatch.create) it would
                // otherwise be entitled to by its own precondition alone.
                var despatchCreateRowCount = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "despatch.create");
                Assert.Equal(0, despatchCreateRowCount);
                var confirmedCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.confirmed.v1");
                Assert.Equal(0, confirmedCount);
            }

            Assert.Equal([reference], creditReleaseCalls.ToArray());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Branch A, ordering 2 — the OTHER ordering: the late
    /// <c>credit.approved.v1</c> arrives WHILE the order is still
    /// <c>stock_reserved</c>, with the operator's own <c>stock.release</c>
    /// already enqueued (accepted, not yet completed) — the scoped
    /// <c>ISagaCommandStore.HasAcceptedOperatorCancelAsync</c> query this is
    /// the one reachable production caller of. Same outcome as the other
    /// ordering: <c>credit.release</c> only, no transition; the compensation
    /// then completes normally once <c>stock.released.v1</c> arrives.
    /// </summary>
    [Fact]
    public async Task StockReserved_LateApproval_BeforeStockReleased()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceLateBefore");
        try
        {
            var creditHoldCalls = new ConcurrentQueue<string>();
            var creditReleaseCalls = new ConcurrentQueue<string>();
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                request =>
                {
                    creditHoldCalls.Enqueue(request.OrderReference);
                    return new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount);
                },
                CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url,
                request =>
                {
                    creditReleaseCalls.Enqueue(request.OrderReference);
                    return new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450);
                },
                CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);

            // A5 (review round 1) — recorded here too, so bullet 8's
            // "records holds and releases" holds in BOTH orderings, not
            // only …AfterStockReleased's.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            Assert.Equal([reference], creditHoldCalls.ToArray());

            const string note = "Buyer cancelled while credit approval was still in flight.";
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, note);
            Assert.Equal("stock_reserved", reply.Status);
            Assert.Equal(["stock_release"], reply.CompensationPlanned);

            // The operator's own stock.release row is enqueued — accepted,
            // not yet completed (deliberately never publish stock.released.v1
            // until AFTER the late approval below).
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            // The late credit.approved.v1 — order STILL stock_reserved, an
            // accepted operator cancel already on record.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);

            // F4 — the late fact must never take the ORDINARY Advance
            // (approve, confirm, dispatch despatch.create) it would
            // otherwise be entitled to by its own precondition alone.
            // Checked HERE, before the "still stock_reserved" claim below —
            // armed by reverting the late check to fall through to the
            // ordinary Advance (record's arming table, F4 guard 1); that
            // mutation trips the status assertion a few lines down for an
            // unrelated reason (the same Advance transitions status
            // synchronously), which would otherwise mask this claim rather
            // than let it fail on its own name.
            await using (var earlyDb = mssql.CreateDbContext(connectionString))
            {
                var earlyDespatchCreateRowCount = await earlyDb.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "despatch.create");
                Assert.Equal(0, earlyDespatchCreateRowCount);
                var earlyConfirmedCount = await earlyDb.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.confirmed.v1");
                Assert.Equal(0, earlyConfirmedCount);
            }

            // Never advanced to credit_approved/confirmed — the ordinary
            // forward-progress Advance this fact would otherwise have taken.
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            // NOW the compensation's own completing fact arrives.
            var stockReleasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("operator_cancelled", row.CancellationReason);

                var creditReleaseRowCount = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "credit.release");
                Assert.Equal(1, creditReleaseRowCount); // exactly one, never a second enqueue from the compensation chain.

                var cancelledCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                Assert.Equal(1, cancelledCount);

                // D2 (review round 1) — the late-approval branch enqueued
                // this order's credit.release row carrying credit.approved.v1's
                // OWN real fact envelope (SagaFactHandler.cs), while the
                // operator's own row is stock.release (the synthetic
                // envelope) — the OTHER interleave where content-versus-
                // position selection decides the outcome. The operator's
                // own note must still reach order.cancelled.v1.
                var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                using var cancelledPayload = JsonDocument.Parse(cancelledRow.Payload);
                Assert.True(
                    cancelledPayload.RootElement.TryGetProperty("note", out var cancelledNoteElement),
                    $"expected the order.cancelled.v1 outbox payload to carry \"note\": \"{note}\", but it carries no note key at all. payload: {cancelledPayload.RootElement.GetRawText()}");
                Assert.Equal(note, cancelledNoteElement.GetString());

                // F4 — re-asserted after the compensation completes too: the
                // absence must hold at BOTH observation points, not only
                // immediately after the late fact.
                var despatchCreateRowCount = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "despatch.create");
                Assert.Equal(0, despatchCreateRowCount);
                var confirmedCount = await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.confirmed.v1");
                Assert.Equal(0, confirmedCount);
            }

            Assert.Equal([reference], creditReleaseCalls.ToArray());
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Id 62 fix round 1, F1 — the SAME "before" ordering as
    /// <see cref="StockReserved_LateApproval_BeforeStockReleased"/>, but
    /// with the SWEEPER DISABLED entirely (<c>Sweeper.Enabled = false</c>):
    /// proves the FAST PATH alone — never the sweeper's 30s-default backstop
    /// — delivers the late <c>credit.release</c> to <c>sent</c>. Before
    /// F1's fix, the late branch published <c>OrderConfirmedBySaga</c>,
    /// whose handler signals a hard-coded <c>DespatchCreate</c> — a claim
    /// against a <c>saga_commands</c> row that does not exist, a silent
    /// no-op (<c>TryClaimAsync</c> affects zero rows) — so the REAL
    /// <c>credit.release</c> row was never signalled at all and depended
    /// entirely on the sweeper, which this test removes.
    /// </summary>
    [Fact]
    public async Task StockReserved_LateApproval_WithTheSweeperDisabled_TheFastPathAloneDeliversCreditRelease()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(
            mssql, kafka, nats, "raceLateNoSweeper",
            configureSaga: options => options.Sweeper.Enabled = false);
        try
        {
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
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

            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            var reply = await CancelAsync(caller, orderId, "Sweeper-disabled proof — the fast path alone must deliver credit.release.");
            Assert.Equal("stock_reserved", reply.Status);

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 97_550), CancellationToken.None);

            // The fast path's own signal must claim and dispatch this row —
            // with the sweeper disabled, nothing else in the system ever
            // will. A 20s wait is comfortably above the fast path's own
            // in-process latency and comfortably below the sweeper's
            // disabled 30s default, so this is never a race with a
            // mechanism this test has already turned off.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var creditReleaseRow = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == "credit.release");
            Assert.Equal("sent", creditReleaseRow.Status);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// The kept operator-first control (bullet 2) — UNAFFECTED by SA-4:
    /// <c>order.despatched.v1</c> was never part of the retired supersede
    /// guard's special case (a plain single-variant <c>Advance</c>,
    /// precondition <c>confirmed</c>), so an operator cancel completing
    /// BEFORE it arrives still leaves R25's ORIGINAL <c>precondition_unmet</c>
    /// path to ignore it correctly — proving the redesign did not turn this
    /// correct no-op into anything else.
    /// </summary>
    [Fact]
    public async Task Confirmed_OperatorFirst_LateDespatchedV1IsIgnoredByPreconditionUnmet()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "raceOperatorFirst");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url, request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url, request => new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 100_000, CreditCode: "CR-000001", HeldAmount: request.Amount.Amount), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url, request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []), CancellationToken.None);
            await using var creditRelease = await StandInSagaResponders.StartCreditReleaseAsync(
                nats.Url, request => new CreditReleaseReplyPayload(true, request.OrderReference, AvailableCreditAfter: 500_00, CreditCode: "CR-000001", Currency: "EUR", ReleasedAmount: 2_450), CancellationToken.None);
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

            var stockReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, stockReleasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "order_cancelled"), CancellationToken.None,
                eventId: stockReleasedFactEventId);

            var creditReleasedFactEventId = Guid.NewGuid();
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.release", "sent", _wait);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", orderId, creditReleasedFactEventId, DateTimeOffset.UtcNow,
                new CreditReleasedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001"), CancellationToken.None,
                eventId: creditReleasedFactEventId);

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
            Assert.Equal(1, await db.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.EventType == "order.despatched.v1" && f.Marker == "precondition_unmet"));
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// F3 — polls until the order's own <c>despatch.create</c> row leaves
    /// <c>pending</c> (any terminal-OR-parked status), never keyed to
    /// <c>"rejected"</c> specifically: under CORRECT code that is always
    /// <c>rejected</c>; under the F3 arm (routing the terminal rejection
    /// through the park path) it is <c>parked</c> instead, and this wait
    /// must still return so the DeadLetteredAt claim gets a chance to run
    /// and fail, rather than the wait itself timing out first.
    /// </summary>
    private async Task WaitForDespatchCreateToLeavePendingAsync(string connectionString, Guid orderId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var status = await db.SagaCommands.AsNoTracking()
                .Where(c => c.OrderId == orderId && c.Command == "despatch.create")
                .Select(c => c.Status)
                .SingleOrDefaultAsync();
            if (status is not null && status != "pending")
            {
                return;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"despatch.create for order {orderId} never left 'pending' within {timeout}.");
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
