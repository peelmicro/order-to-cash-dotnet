using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.1 — the transactional unit's composition, over FAKES (no
/// database): duplicate ⇒ nothing; unknown order ⇒ SO8 record; precondition
/// unmet ⇒ R25 record with both observed and expected status; happy step ⇒
/// save then enqueue, and the returned result names the enqueued command.
/// </summary>
public sealed class SagaFactHandlerTests
{
    [Fact]
    public async Task Duplicate_ReturnsDuplicateAndDoesNothingAtAll()
    {
        var orders = new FakeOrderRepository();
        var runner = new FakeIdempotentSagaRunner { ReturnDuplicate = true };
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("order.placed.v1", Guid.NewGuid());
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Duplicate, result.Outcome);
        Assert.Null(result.Enqueued);
        Assert.Empty(ignoredFacts.Records);
        Assert.Empty(store.Enqueued);
        Assert.False(orders.SaveChangesCalled);
        Assert.False(orders.GetByIdWasCalled);
    }

    [Fact]
    public async Task UnknownOrder_RecordsUnknownOrderAndDoesNotSaveOrEnqueue()
    {
        var orders = new FakeOrderRepository { OrderToReturn = null };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var correlationId = Guid.NewGuid();
        var fact = BuildFact("order.placed.v1", correlationId);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Ignored, result.Outcome);
        Assert.Null(result.Enqueued);
        var record = Assert.Single(ignoredFacts.Records);
        Assert.Equal(SagaIgnoredFactMarker.UnknownOrder, record.Marker);
        Assert.Null(record.OrderId);
        Assert.Equal(correlationId, record.CorrelationId);
        Assert.Null(record.ObservedStatus);
        Assert.Null(record.ExpectedStatus);
        Assert.False(orders.SaveChangesCalled);
        Assert.Empty(store.Enqueued);
    }

    [Fact]
    public async Task PreconditionUnmet_RecordsBothObservedAndExpectedStatusAndDoesNotSaveOrEnqueue()
    {
        // order.despatched.v1 requires Confirmed; supply an order at Placed.
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("order.despatched.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Ignored, result.Outcome);
        Assert.Null(result.Enqueued);
        var record = Assert.Single(ignoredFacts.Records);
        Assert.Equal(SagaIgnoredFactMarker.PreconditionUnmet, record.Marker);
        Assert.Equal(order.Id.Value, record.OrderId);
        Assert.Equal(OrderStatus.Placed, record.ObservedStatus);
        Assert.Equal(OrderStatus.Confirmed, record.ExpectedStatus);
        Assert.False(orders.SaveChangesCalled);
        Assert.Empty(store.Enqueued);
    }

    [Fact]
    public async Task HappyStep_SavesThenEnqueuesAndTheReturnedResultNamesTheEnqueuedCommand()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("order.placed.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.NotNull(result.Enqueued);
        Assert.Equal(order.Id.Value, result.Enqueued!.OrderId);
        Assert.Equal(SagaCommandKind.StockReserve, result.Enqueued.Command);

        Assert.True(orders.SaveChangesCalled);
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.StockReserve, enqueued.Command);
        Assert.Equal(fact.EventId, enqueued.TriggeringEventId);

        // Save happens BEFORE enqueue.
        Assert.True(orders.SaveChangesCalledAtSequence < store.EnqueueCalledAtSequence);
        Assert.Empty(ignoredFacts.Records);
    }

    /// <summary>
    /// OR5/design.md §7, ported cases 74-78 (the completed-outcome case).
    /// <c>otc_saga_completion_ms</c> records EXACTLY ONE completion,
    /// measured from the order's own <c>OrderDate</c>, tagged
    /// <c>outcome=completed</c>. Review round 2, R8: this comment
    /// previously claimed "ported cases 74-78" for ALL FIVE of #7's cases
    /// while only three of the five methods existed (this one, plus the
    /// two "records nothing" cases below) — the direct-cancel and
    /// compensation-not-two cases were missing. All five now exist, this
    /// method through
    /// <see cref="OtcSagaCompletionMs_TheCompensationCompletingCancel_RecordsExactlyOneNotTwo"/>.
    /// </summary>
    [Fact]
    public async Task OtcSagaCompletionMs_ARealCompletingTransition_RecordsExactlyOneCompletionTaggedCompleted()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Paid);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var completionRecorder = new FakeSagaCompletionRecorder();
        var laterClock = new FakeClock(OrderTestData.Now.AddMinutes(37));
        var handler = new SagaFactHandler(orders, runner, ignoredFacts, store, completionRecorder, new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), laterClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

        var fact = BuildFact("credit.released.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        var recorded = Assert.Single(completionRecorder.Recorded);
        Assert.Equal("completed", recorded.Outcome);
        // Exact, independently computable — laterClock.UtcNow minus the order's own OrderDate.
        Assert.Equal(laterClock.UtcNow - order.OrderDate, recorded.Duration);
    }

    /// <summary>A3i — a NON-closing step (order.placed.v1, Placed -&gt; StockReserved) records NOTHING.</summary>
    [Fact]
    public async Task OtcSagaCompletionMs_ANonClosingStep_RecordsNothing()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var completionRecorder = new FakeSagaCompletionRecorder();
        var handler = new SagaFactHandler(orders, runner, ignoredFacts, store, completionRecorder, new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

        var fact = BuildFact("order.placed.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Empty(completionRecorder.Recorded);
    }

    /// <summary>A3i — an IGNORED fact (precondition unmet) records NOTHING.</summary>
    [Fact]
    public async Task OtcSagaCompletionMs_AnIgnoredFact_RecordsNothing()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var completionRecorder = new FakeSagaCompletionRecorder();
        var handler = new SagaFactHandler(orders, runner, ignoredFacts, store, completionRecorder, new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

        // order.despatched.v1 requires Confirmed; the order is at Placed.
        var fact = BuildFact("order.despatched.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Ignored, result.Outcome);
        Assert.Empty(completionRecorder.Recorded);
    }

    /// <summary>
    /// Review round 2, D5 rows 65–66/74–78 — #7's
    /// <c>saga-fact-handler-saga-completion-metrics.spec</c> case "a direct
    /// cancel records EXACTLY ONE cancellation", missing here before this
    /// round (the doc comment on <see cref="OtcSagaCompletionMs_ARealCompletingTransition_RecordsExactlyOneCompletionTaggedCompleted"/>
    /// claimed "ported cases 74-78" while only the completed-outcome case
    /// existed). <c>stock.rejected.v1</c> at <c>Placed</c> cancels the order
    /// in ONE step (no compensation chain) — armed by hard-coding
    /// <c>SagaFactHandler.cs:118</c>'s <c>outcomeTag</c> to
    /// <c>"completed"</c>, which left the whole <c>Orders.UnitTests</c>
    /// suite green before this case existed.
    /// </summary>
    [Fact]
    public async Task OtcSagaCompletionMs_ADirectCancel_RecordsExactlyOneCancellationTaggedCancelled()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var completionRecorder = new FakeSagaCompletionRecorder();
        var laterClock = new FakeClock(OrderTestData.Now.AddMinutes(11));
        var handler = new SagaFactHandler(orders, runner, ignoredFacts, store, completionRecorder, new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), laterClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

        var fact = BuildFact("stock.rejected.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var recorded = Assert.Single(completionRecorder.Recorded);
        Assert.Equal("cancelled", recorded.Outcome);
        Assert.Equal(laterClock.UtcNow - order.OrderDate, recorded.Duration);
    }

    /// <summary>
    /// Review round 2, D5 rows 65–66/74–78 — #7's own second missing case:
    /// "the compensation-completing cancel records EXACTLY ONE, not two".
    /// SA-4's two-step operator-cancel compensation (saga.md §4.3):
    /// <c>stock.released.v1</c>'s <c>CreditApproved</c> variant is a no-op
    /// <c>Advance</c> (records nothing — proven first, below), then
    /// <c>credit.released.v1</c>'s <c>CreditApproved</c> variant is the
    /// <c>Cancel</c> that actually completes the order — recorded exactly
    /// once, on the SAME <see cref="FakeSagaCompletionRecorder"/> instance
    /// across both calls, so a regression that also records on the
    /// no-op step would show TWO entries, not one.
    /// </summary>
    [Fact]
    public async Task OtcSagaCompletionMs_TheCompensationCompletingCancel_RecordsExactlyOneNotTwo()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.CreditApproved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var completionRecorder = new FakeSagaCompletionRecorder();
        var laterClock = new FakeClock(OrderTestData.Now.AddMinutes(19));
        var handler = new SagaFactHandler(orders, runner, ignoredFacts, store, completionRecorder, new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), laterClock, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

        var stockReleasedFact = BuildFact("stock.released.v1", order.Id.Value);
        var stockResult = await handler.HandleAsync(stockReleasedFact, CancellationToken.None);
        Assert.Equal(SagaFactOutcome.Processed, stockResult.Outcome);
        Assert.Equal(OrderStatus.CreditApproved, order.Status);
        Assert.Empty(completionRecorder.Recorded);

        var creditReleasedPayload = new CreditReleasedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 2_450, 100_000, "order_cancelled", "CR-000001");
        var creditReleasedFact = BuildFactWithPayload("credit.released.v1", order.Id.Value, creditReleasedPayload);
        var creditResult = await handler.HandleAsync(creditReleasedFact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, creditResult.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var recorded = Assert.Single(completionRecorder.Recorded);
        Assert.Equal("cancelled", recorded.Outcome);
        Assert.Equal(laterClock.UtcNow - order.OrderDate, recorded.Duration);
    }

    /// <summary>
    /// <c>observability_reliability</c>, design.md §4.2 (ledger L15) — the
    /// triggering fact's RAW bytes and source topic are threaded through
    /// <c>SagaFact</c> into <c>ISagaCommandStore.EnqueueAsync</c> UNCHANGED
    /// — the exact array reference this test constructs, never a
    /// re-serialised copy that would happen to contain the same bytes.
    /// </summary>
    [Fact]
    public async Task OR3_ThreadsTheTriggeringFactsRawEnvelopeBytesAndTopicIntoEnqueueAsync_Verbatim()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var envelopeBytes = System.Text.Encoding.UTF8.GetBytes("""{"eventType":"order.placed.v1"}""");
        const string sourceTopic = "otc.orders.facts.v1";
        var fact = BuildFact("order.placed.v1", order.Id.Value, envelopeBytes, sourceTopic);

        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        var triggeringFact = Assert.Single(store.EnqueuedTriggeringFacts);
        Assert.Same(envelopeBytes, triggeringFact.Envelope); // the SAME array reference — never a re-serialised copy.
        Assert.Equal(sourceTopic, triggeringFact.Topic);
    }

    /// <summary>
    /// Review round 2, D1: <c>SagaCommandRequestFactory.StockReleaseReasonFor</c> is called from exactly ONE
    /// production site — <c>SagaFactHandler.HandleAsync</c>'s <c>command == SagaCommandKind.StockRelease</c> branch
    /// (<c>SagaFactHandler.cs:114</c>) — so this drives the real handler end to end and opens the ENQUEUED
    /// payload's <c>Reason</c>, rather than re-implementing the switch or calling the factory directly. Transposing
    /// <c>StockReleaseReasonFor</c>'s two arms must fail this test.
    /// </summary>
    [Fact]
    public async Task CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("credit.rejected.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.StockRelease, enqueued.Command);
        var payload = RpcJson.Deserialize<StockReleaseRequestPayload>(System.Text.Encoding.UTF8.GetBytes(enqueued.Payload));
        Assert.Equal("credit_rejected", payload.Reason);
    }

    /// <summary>
    /// SA-4's own step — the mirror image of the ABOVE, on the OTHER
    /// completing fact: <c>stock.released.v1</c>'s <c>credit_approved</c>/
    /// <c>confirmed</c> variant is a no-op <see cref="SagaStep.Advance"/>
    /// that owes <see cref="SagaCommandKind.CreditRelease"/> — the CONTESTED
    /// resource (stock) has already been released (this fact IS that
    /// release), so the reverse-order-of-acquisition chain's second hop is
    /// now due. Reached through <c>SagaCommandRequestFactory.BuildJson</c>'s
    /// generic overload, not the reason-aware <c>BuildStockReleaseJson</c> —
    /// <c>credit.release</c> has no caller-chosen reason.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.CreditApproved)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task StockReleasedV1_CreditApprovedOrConfirmedVariant_OwesCreditReleaseAsANoOpAdvance(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("stock.released.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(status, order.Status); // no transition — a no-op Advance.
        Assert.True(orders.SaveChangesCalled);
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);
        var payload = RpcJson.Deserialize<CreditReleaseRequestPayload>(System.Text.Encoding.UTF8.GetBytes(enqueued.Payload));
        Assert.Equal(order.OrderReference.Value, payload.OrderReference);
        Assert.Equal(order.RetailerCode, payload.RetailerCode);
        Assert.Equal(order.CompanyCode, payload.CompanyCode);
        Assert.Empty(ignoredFacts.Records);
    }

    /// <summary>
    /// SA-4's completing step: <c>credit.released.v1</c>'s <c>credit_approved</c>/
    /// <c>confirmed</c> variant CANCELS (the inverse of the pre-SA-4 shape,
    /// where this fact type was the no-op first hop and <c>stock.released.v1</c>
    /// completed) — reason <c>operator_cancelled</c>, mapped from the fact's
    /// own <c>order_cancelled</c> wire reason
    /// (<see cref="SagaStepTable.MapCreditReleaseReason"/>), and compensation
    /// steps recorded in RELEASE order: <c>stock_released</c> (synthesised,
    /// no <c>eventId</c> — the earlier fact's own id is not available, see
    /// <see cref="SagaStepTable.CompensationStepsFromStockThenCreditRelease"/>'s
    /// own remarks) then <c>credit_released</c> (THIS fact's own id).
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.CreditApproved)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task CreditReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithStepsInStockThenCreditOrder(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var payload = new CreditReleasedPayload(order.OrderReference.Value, order.RetailerCode, order.CompanyCode, "EUR", 2_450, 100_000, "order_cancelled", "CR-000001");
        var fact = BuildFactWithPayload("credit.released.v1", order.Id.Value, payload);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.OperatorCancelled, order.CancellationReason);
        Assert.Empty(store.Enqueued); // Cancel steps never enqueue anything.

        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        var steps = cancelled.CompensationSteps;
        Assert.Equal(2, steps.Count);
        Assert.Equal(CompensationStepKind.StockReleased, steps[0].Step);
        Assert.Null(steps[0].EventId);
        Assert.Equal(CompensationStepKind.CreditReleased, steps[1].Step);
        Assert.Equal(fact.EventId, steps[1].EventId!.Value.Value);
    }

    /// <summary>
    /// Id 71's core wiring, <c>stock_reserved</c> branch —
    /// <c>ApplyStepAsync</c>'s <see cref="SagaStep.Cancel"/> case reads the
    /// note back from <see cref="ISagaCommandStore.FindOperatorCancelNoteAsync"/>,
    /// keyed by the ORDER's own id (never fabricated, never a stray field —
    /// <see cref="FakeSagaCommandStore.FindOperatorCancelNoteCalls"/> records
    /// the actual argument), and threads the EXACT text onto
    /// <c>OrderCancelled.Note</c> — bracketed, not merely non-null.
    /// </summary>
    [Fact]
    public async Task StockReservedVariant_StockReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        const string note = "Reservation released — buyer called to cancel before despatch.";
        var store = new FakeSagaCommandStore { NoteToReturn = note };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var stockReleasedPayload = new StockReleasedPayload("ORD-000001", "COMPANY1", [], "order_cancelled");
        var fact = BuildFactWithPayload("stock.released.v1", order.Id.Value, stockReleasedPayload);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains(order.Id.Value, store.FindOperatorCancelNoteCalls);
        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        Assert.Equal(note, cancelled.Note);
    }

    /// <summary>
    /// Id 71's core wiring, <c>credit_approved</c>/<c>confirmed</c> branch —
    /// under SA-4 (ruled 2026-09-11) the TWO-hop chain runs
    /// <c>stock.released.v1</c> FIRST, then <c>credit.released.v1</c>,
    /// reading the note back only on the fact that actually completes the
    /// cancellation (the FIRST hop, <c>stock.released.v1</c>, is a no-op
    /// <see cref="SagaStep.Advance"/> that owes <c>credit.release</c> next,
    /// never touching the store's note lookup at all).
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.CreditApproved)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task CreditApprovedOrConfirmedVariant_CreditReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        const string note = "Reservation and credit both released — customer changed the delivery date twice.";
        var store = new FakeSagaCommandStore { NoteToReturn = note };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var stockReleasedFact = BuildFact("stock.released.v1", order.Id.Value);
        var stockResult = await handler.HandleAsync(stockReleasedFact, CancellationToken.None);
        Assert.Equal(SagaFactOutcome.Processed, stockResult.Outcome);
        Assert.Empty(store.FindOperatorCancelNoteCalls); // the no-op Advance never looks it up.

        var creditReleasedPayload = new CreditReleasedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 2_450, 100_000, "order_cancelled", "CR-000001");
        var creditReleasedFact = BuildFactWithPayload("credit.released.v1", order.Id.Value, creditReleasedPayload);
        var creditResult = await handler.HandleAsync(creditReleasedFact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, creditResult.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains(order.Id.Value, store.FindOperatorCancelNoteCalls);
        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        Assert.Equal(note, cancelled.Note);
    }

    /// <summary>
    /// Id 71 bullet 3 (unit half) — a SAGA-DECIDED cancellation
    /// (<c>stock_rejected</c>) still calls through the SAME lookup (no
    /// branch on "which cancel is this" is written anywhere), but the store
    /// genuinely has nothing to return — no operator ever compensated this
    /// order — so <c>OrderCancelled.Note</c> stays <see langword="null"/>.
    /// </summary>
    [Fact]
    public async Task StockRejectedV1_DirectCancel_CarriesNoOperatorNoteWhenTheStoreHasNone()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore { NoteToReturn = null };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("stock.rejected.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        Assert.Null(cancelled.Note);
    }

    /// <summary>Same shape, the OTHER saga-decided path — <c>credit.rejected.v1</c> then <c>stock.released.v1</c> (reason <c>credit_rejected</c>).</summary>
    [Fact]
    public async Task CreditRejectedV1_ThenStockReleasedV1_CarriesNoOperatorNoteWhenTheStoreHasNone()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore { NoteToReturn = null };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var creditRejectedFact = BuildFact("credit.rejected.v1", order.Id.Value);
        await handler.HandleAsync(creditRejectedFact, CancellationToken.None);

        var stockReleasedPayload = new StockReleasedPayload("ORD-000001", "COMPANY1", [], "credit_rejected");
        var stockReleasedFact = BuildFactWithPayload("stock.released.v1", order.Id.Value, stockReleasedPayload);
        var result = await handler.HandleAsync(stockReleasedFact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.CreditRejected, order.CancellationReason);
        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        Assert.Null(cancelled.Note);
    }

    /// <summary>
    /// SA-4's own new clause (saga.md §4.3, "A credit approval that arrives
    /// after the cancellation"), <c>stock_reserved</c> shape — a late
    /// <c>credit.approved.v1</c> for a hold issued before the operator's own
    /// (already-accepted, not-yet-completed) cancellation issues
    /// <c>credit.release</c> and NOTHING else: no transition, no
    /// <c>despatch.create</c>. Checked BEFORE the generic status-precondition
    /// dispatch (<see cref="SagaFactHandler"/>'s own <c>CreditApprovedEventType</c>
    /// remarks) — the order's status DOES match the ordinary Advance's own
    /// precondition, which is exactly why this needs its own check.
    /// </summary>
    [Fact]
    public async Task CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtStockReserved_IssuesCreditReleaseOnly()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore { AcceptedOperatorCancelToReturn = true };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("credit.approved.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.NotNull(result.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, result.Enqueued!.Command);
        Assert.Equal(OrderStatus.StockReserved, order.Status); // never advanced to Confirmed.
        Assert.False(orders.SaveChangesCalled); // no domain mutation — the order itself is untouched.
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);
        Assert.Contains(order.Id.Value, store.HasAcceptedOperatorCancelCalls);
        Assert.Empty(ignoredFacts.Records); // Processed, not ignored — a real, durable effect.
    }

    /// <summary>SA-4's OTHER ordering — the compensation has already completed (order already <c>cancelled</c>, reason <c>operator_cancelled</c>); the store query is never even asked, since the order's own reason already answers it.</summary>
    [Fact]
    public async Task CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtCancelledOperatorCancelled_IssuesCreditReleaseOnly()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Cancelled, cancellationReason: CancellationReason.OperatorCancelled);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("credit.approved.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.NotNull(result.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, result.Enqueued!.Command);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);
        Assert.Empty(store.HasAcceptedOperatorCancelCalls); // short-circuited by the order's own reason.
        Assert.Empty(ignoredFacts.Records);
    }

    /// <summary>Every OTHER stale combination at <c>cancelled</c> — a SAGA-decided reason, never an operator's — still falls to R25's ORIGINAL <c>precondition_unmet</c> ignore, unaffected by SA-4's new clause.</summary>
    [Theory]
    [InlineData(CancellationReason.StockRejected)]
    [InlineData(CancellationReason.CreditRejected)]
    public async Task CreditApprovedV1_AtCancelledWithASagaDecidedReason_IsIgnoredByPreconditionUnmet(CancellationReason reason)
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Cancelled, cancellationReason: reason);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("credit.approved.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Ignored, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(store.Enqueued);
        var record = Assert.Single(ignoredFacts.Records);
        Assert.Equal(SagaIgnoredFactMarker.PreconditionUnmet, record.Marker);
    }

    /// <summary>
    /// Review round 1, A2 — the two facts SA-4 REWIRED (<c>stock.released.v1</c>
    /// and <c>credit.released.v1</c> both now own a conditional
    /// <c>credit_approved</c>/<c>confirmed</c> variant) had no unit case at
    /// all for the ONE precondition <see cref="SagaStepTable"/> genuinely
    /// lacks a variant for: <c>Cancelled</c> with reason
    /// <c>OperatorCancelled</c> (the compensation having ALREADY completed
    /// before either fact arrives again — a duplicate or late redelivery).
    /// R25's original <c>precondition_unmet</c> path must still ignore it
    /// cleanly: no throw, nothing enqueued, no status change. Armed by the
    /// review's own M5 (record's Fix round 3 section).
    /// </summary>
    [Theory]
    [InlineData("stock.released.v1")]
    [InlineData("credit.released.v1")]
    public async Task StockReleasedOrCreditReleasedV1_AtCancelledOperatorCancelled_IsIgnoredByPreconditionUnmetWithNoThrow(string eventType)
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Cancelled, cancellationReason: CancellationReason.OperatorCancelled);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore();
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact(eventType, order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Ignored, result.Outcome);
        Assert.Null(result.Enqueued);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(store.Enqueued);
        var record = Assert.Single(ignoredFacts.Records);
        Assert.Equal(SagaIgnoredFactMarker.PreconditionUnmet, record.Marker);
    }

    /// <summary>The ordinary case, unaffected: at <c>stock_reserved</c> with NO accepted operator cancel, <c>credit.approved.v1</c> takes the NORMAL Advance — approve, confirm, dispatch <c>despatch.create</c>.</summary>
    [Fact]
    public async Task CreditApprovedV1_AtStockReservedWithNoAcceptedOperatorCancel_TakesTheNormalAdvance()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var ignoredFacts = new FakeSagaIgnoredFactRecorder();
        var store = new FakeSagaCommandStore { AcceptedOperatorCancelToReturn = false };
        var handler = BuildHandler(orders, runner, ignoredFacts, store);

        var fact = BuildFact("credit.approved.v1", order.Id.Value);
        var result = await handler.HandleAsync(fact, CancellationToken.None);

        Assert.Equal(SagaFactOutcome.Processed, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.True(orders.SaveChangesCalled);
        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.DespatchCreate, enqueued.Command);
        Assert.Contains(order.Id.Value, store.HasAcceptedOperatorCancelCalls);
        Assert.Empty(ignoredFacts.Records);
    }

    private static SagaFactHandler BuildHandler(FakeOrderRepository orders, FakeIdempotentSagaRunner runner, FakeSagaIgnoredFactRecorder ignoredFacts, FakeSagaCommandStore store, FakeSagaCompletionRecorder? completionRecorder = null) =>
        new(orders, runner, ignoredFacts, store, completionRecorder ?? new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

    /// <summary>Shared with <c>SagaFactCommandHandlerTests</c>' own private nested copy — this one is exposed (not private) so <c>SagaCompletionMetricTests</c> can inspect <see cref="Recorded"/> after driving <see cref="BuildHandler"/> through a real transition.</summary>
    internal sealed class FakeSagaCompletionRecorder : ISagaCompletionRecorder
    {
        public List<(string Outcome, TimeSpan Duration)> Recorded { get; } = [];

        public void Record(string outcome, TimeSpan duration) => Recorded.Add((outcome, duration));
    }

    private static SagaFact BuildFact(string eventType, Guid correlationId, byte[]? triggeringEventEnvelope = null, string? triggeringEventTopic = null) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        AggregateId: correlationId,
        CorrelationId: correlationId,
        CausationId: Guid.NewGuid(),
        OccurredAt: OrderTestData.Now.AddMinutes(5),
        Payload: new object(),
        TriggeringEventEnvelope: triggeringEventEnvelope,
        TriggeringEventTopic: triggeringEventTopic);

    /// <summary>Same shape as <see cref="BuildFact"/>, for the one row (<c>stock.released.v1</c>) whose <c>SagaStep.Cancel</c> reads a typed payload (<c>SagaStepTable.MapReason</c>) rather than only <c>EventType</c>/<c>OccurredAt</c>.</summary>
    private static SagaFact BuildFactWithPayload(string eventType, Guid correlationId, object payload) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        AggregateId: correlationId,
        CorrelationId: correlationId,
        CausationId: Guid.NewGuid(),
        OccurredAt: OrderTestData.Now.AddMinutes(5),
        Payload: payload,
        TriggeringEventEnvelope: null,
        TriggeringEventTopic: null);

    private static int _sequence;

    private sealed class FakeIdempotentSagaRunner : IIdempotentSagaRunner
    {
        public bool ReturnDuplicate { get; set; }

        public async Task<IdempotentSagaRunOutcome> RunOnceAsync(Guid eventId, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
        {
            if (ReturnDuplicate)
            {
                return IdempotentSagaRunOutcome.Duplicate;
            }

            await work(cancellationToken);
            return IdempotentSagaRunOutcome.Processed;
        }
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Order? OrderToReturn { get; set; }

        public bool SaveChangesCalled { get; private set; }

        public bool GetByIdWasCalled { get; private set; }

        public int SaveChangesCalledAtSequence { get; private set; } = -1;

        public Task AddAsync(Order order, Guid? requestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken)
        {
            GetByIdWasCalled = true;
            return Task.FromResult(OrderToReturn);
        }

        public Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCalled = true;
            SaveChangesCalledAtSequence = System.Threading.Interlocked.Increment(ref _sequence);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaIgnoredFactRecorder : ISagaIgnoredFactRecorder
    {
        public List<SagaIgnoredFactRecord> Records { get; } = [];

        public Task RecordAsync(SagaIgnoredFactRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public List<(Guid OrderId, string OrderReference, SagaCommandKind Command, string Payload, Guid TriggeringEventId)> Enqueued { get; } = [];

        public List<(byte[]? Envelope, string? Topic)> EnqueuedTriggeringFacts { get; } = [];

        public EnqueueOutcome OutcomeToReturn { get; set; } = EnqueueOutcome.Enqueued;

        public int EnqueueCalledAtSequence { get; private set; } = -1;

        /// <summary>
        /// Id 71 — what <see cref="FindOperatorCancelNoteAsync"/> returns,
        /// settable per test. <see cref="FindOperatorCancelNoteCalls"/>
        /// records every <paramref name="orderId"/> asked for, so a test can
        /// prove the lookup actually happened (never merely that
        /// <c>Order.Cancel</c> received SOME note).
        /// </summary>
        public string? NoteToReturn { get; set; }

        public List<Guid> FindOperatorCancelNoteCalls { get; } = [];

        /// <summary>
        /// Id 62/SA-4 — what <see cref="HasAcceptedOperatorCancelAsync"/>
        /// returns, settable per test; defaults <see langword="false"/> so
        /// every EXISTING test (none of which is about the late-approval
        /// unwind) is unaffected. <see cref="HasAcceptedOperatorCancelCalls"/>
        /// records every <c>orderId</c> asked for, so a test can prove the
        /// query actually ran — never merely that the fact was handled some
        /// way.
        /// </summary>
        public bool AcceptedOperatorCancelToReturn { get; set; }

        public List<Guid> HasAcceptedOperatorCancelCalls { get; } = [];

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken)
        {
            Enqueued.Add((orderId, orderReference, command, payload, triggeringEventId));
            EnqueuedTriggeringFacts.Add((triggeringEventEnvelope, triggeringEventTopic));
            EnqueueCalledAtSequence = System.Threading.Interlocked.Increment(ref _sequence);
            return Task.FromResult(OutcomeToReturn);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken)
        {
            FindOperatorCancelNoteCalls.Add(orderId);
            return Task.FromResult(NoteToReturn);
        }

        public Task<bool> HasAcceptedOperatorCancelAsync(Guid orderId, CancellationToken cancellationToken)
        {
            HasAcceptedOperatorCancelCalls.Add(orderId);
            return Task.FromResult(AcceptedOperatorCancelToReturn);
        }
    }
}
