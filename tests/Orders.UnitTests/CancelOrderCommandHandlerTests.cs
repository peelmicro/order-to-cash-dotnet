using System.Text.Json;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The <c>orders.cancel</c> application logic (feature
/// <c>orders_cancel_responder</c>) — all four branches of
/// <c>saga.md</c> §4.3's generalisation table, against fakes (no NATS, no
/// database). Reuses <c>PlaceOrderTestDoubles.cs</c>'s top-level
/// <c>FakeOrderRepository</c>/<c>FakeUnitOfWork</c>/<c>FakeClock</c> —
/// its own <see cref="ISagaCommandStore"/>/<see cref="ISagaCommandSignal"/>
/// fakes are the only new doubles this feature needs.
/// </summary>
public sealed class CancelOrderCommandHandlerTests
{
    [Fact]
    public async Task Placed_CancelsImmediately_ReasonOperatorCancelledAndNoCompensationPlanned()
    {
        var order = OrderTestData.PlacedOrder();
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now.AddMinutes(5));

        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: "please cancel"), CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.Equal(CancellationReason.OperatorCancelled, result.CancellationReason);
        Assert.Empty(result.CompensationPlanned);

        // The aggregate itself, not just the returned result, actually
        // transitioned and was saved — reused Order.Cancel verbatim.
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.OperatorCancelled, order.CancellationReason);
        Assert.Equal(1, orders.SaveChangesCallCount);

        // No compensation for an order that never acquired anything.
        Assert.Empty(store.Enqueued);
        Assert.Empty(signal.Signalled);
    }

    /// <summary>
    /// SA-2/feature <c>operator_note_reaches_the_timeline</c> bullet 1's
    /// handler half — the immediate branch threads
    /// <see cref="CancelOrderCommand.Note"/> onto the raised
    /// <see cref="OrderCancelled"/> domain event, bracketed to the exact
    /// text supplied (provenance, not merely non-null).
    /// </summary>
    [Fact]
    public async Task Placed_CancelsImmediately_ThreadsTheSuppliedNoteOntoOrderCancelled()
    {
        var order = OrderTestData.PlacedOrder();
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now.AddMinutes(5));
        const string note = "Cancelled at buyer's written request.";

        await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: note), CancellationToken.None);

        var cancelled = Assert.Single(order.DomainEvents.OfType<OrderToCash.Orders.Domain.Events.OrderCancelled>());
        Assert.Equal(note, cancelled.Note);
    }

    /// <summary>
    /// Acceptance bullet 3, exhaustive over every status <c>Order.Cancel</c>
    /// itself refuses (including an ALREADY-cancelled order) — a domain
    /// error, never a 503, and the order is left byte-identical: no new
    /// status-set check is written in the handler, this is
    /// <see cref="Order.Cancel"/>'s own R8/R9/O5/O7 guard, reused verbatim.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Despatched)]
    [InlineData(OrderStatus.Invoiced)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Terminal_ThrowsOrderNotCancellableAndLeavesTheOrderUntouched(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status, cancellationReason: status == OrderStatus.Cancelled ? CancellationReason.StockRejected : null);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now.AddMinutes(5));

        var error = await Assert.ThrowsAsync<OrderNotCancellableError>(
            () => handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: null), CancellationToken.None));

        Assert.Equal(status, error.From);
        Assert.Equal(status, order.Status);
        Assert.Equal(0, orders.SaveChangesCallCount);
        Assert.Empty(store.Enqueued);
        Assert.Empty(signal.Signalled);
    }

    /// <summary>
    /// The <c>stock_reserved</c> branch — enqueues <c>stock.release</c> with
    /// reason <c>order_cancelled</c> (deserialised and checked field-by-field
    /// from the enqueued payload, not merely "a row exists": a hardcoded
    /// <c>credit_rejected</c> reason — the value R27's UNRELATED branch
    /// uses — would satisfy "one row enqueued" while carrying the wrong
    /// reason onto the wire). The order stays <c>stock_reserved</c>: the
    /// EXISTING <c>stock.released.v1</c> step completes the cancellation
    /// later, not this handler.
    /// </summary>
    [Fact]
    public async Task StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_StatusUnchangedAndOnlyStockReleasePlanned()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var now = OrderTestData.Now.AddMinutes(5);
        var handler = BuildHandler(orders, store, signal, now);
        const string note = "Buyer asked to cancel before despatch.";

        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: note), CancellationToken.None);

        Assert.Equal(OrderStatus.StockReserved, result.Status);
        Assert.Null(result.CancellationReason);
        Assert.Equal(["stock_release"], result.CompensationPlanned);
        Assert.Equal(OrderStatus.StockReserved, order.Status);
        Assert.Equal(0, orders.SaveChangesCallCount); // no aggregate mutation — nothing to save

        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(order.Id.Value, enqueued.OrderId);
        Assert.Equal(order.OrderReference.Value, enqueued.OrderReference);
        Assert.Equal(SagaCommandKind.StockRelease, enqueued.Command);

        var payload = JsonSerializer.Deserialize<StockReleaseRequestPayload>(enqueued.Payload, JsonWire.Options)!;
        Assert.Equal(order.OrderReference.Value, payload.OrderReference);
        Assert.Equal("order_cancelled", payload.Reason);

        // Id 71 — porting #7's buildTriggeringEnvelope (cancel-order.handler.ts:175,
        // :272-286) verbatim: a synthetic orders.cancel.requested envelope,
        // never null (R29's dead-letter clause needs it to republish
        // anything, and this row now also carries the operator's note back
        // for SagaFactHandler to read once the compensation completes).
        var triggeringFact = Assert.Single(store.EnqueuedTriggeringFacts);
        Assert.Equal(OrdersFactTopic.Name, triggeringFact.Topic);
        Assert.NotNull(triggeringFact.Envelope);
        var envelope = JsonSerializer.Deserialize<Envelope<OperatorCancelRequestedPayload>>(triggeringFact.Envelope!, JsonWire.Options)!;
        Assert.Equal("orders.cancel.requested", envelope.EventType);
        Assert.Equal(order.Id.Value, envelope.AggregateId);
        Assert.Equal(order.Id.Value, envelope.CorrelationId);
        Assert.Equal(enqueued.TriggeringEventId, envelope.EventId);
        Assert.Equal(enqueued.TriggeringEventId, envelope.CausationId);
        Assert.Equal(order.Id.Value, envelope.Payload.OrderId);
        Assert.Equal("operator_cancelled", envelope.Payload.Reason);
        Assert.Equal(note, envelope.Payload.Note);

        var signalled = Assert.Single(signal.Signalled);
        Assert.Equal(order.Id.Value, signalled.OrderId);
        Assert.Equal(SagaCommandKind.StockRelease, signalled.Command);
    }

    /// <summary>
    /// Id 71 — when the operator supplies no note, the synthetic envelope
    /// still exists (R29 needs it regardless), but JsonWire's own
    /// null-omission means the wire carries no <c>note</c> key at all —
    /// never a present, empty, or literal-null one.
    /// </summary>
    [Fact]
    public async Task StockReserved_NoNoteSupplied_TheSyntheticEnvelopeOmitsTheNoteKeyEntirely()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now.AddMinutes(5));

        await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: null), CancellationToken.None);

        var triggeringFact = Assert.Single(store.EnqueuedTriggeringFacts);
        using var document = JsonDocument.Parse(triggeringFact.Envelope!);
        var payloadElement = document.RootElement.GetProperty("payload");
        Assert.False(payloadElement.TryGetProperty("note", out _), "the note key must be OMITTED, not present with a null/empty value.");
    }

    /// <summary>
    /// A duplicate-key hit on enqueue (the command is already owed/sent —
    /// design.md §6.3) must not signal a second fast-path dispatch, exactly
    /// as every fact-driven dispatch-owed handler already refuses to. The
    /// reply shape is unaffected — the caller still sees what IS planned,
    /// idempotently.
    /// </summary>
    [Fact]
    public async Task StockReserved_AlreadyEnqueued_DoesNotSignalASecondFastPathDispatch()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore { OutcomeToReturn = EnqueueOutcome.AlreadyEnqueued };
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now);

        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: null), CancellationToken.None);

        Assert.Equal(["stock_release"], result.CompensationPlanned);
        Assert.Empty(signal.Signalled);
    }

    /// <summary>
    /// The <c>credit_approved</c>/<c>confirmed</c> branch — enqueues
    /// <c>credit.release</c> ONLY (never <c>stock.release</c> too: that
    /// follows later, once <c>credit.released.v1</c> arrives and the
    /// EXTENDED <see cref="SagaStepTable"/> owes it — this handler issues
    /// the FIRST step of the reverse-order-of-acquisition chain, not both).
    /// The reply names BOTH planned releases, credit first (saga.md §4.3).
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.CreditApproved)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_StatusUnchangedAndBothReleasesPlannedInReverseOrderOfAcquisition(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now.AddMinutes(5));
        const string note = "Retailer requested cancellation after credit was already approved.";

        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id.Value, Note: note), CancellationToken.None);

        Assert.Equal(status, result.Status);
        Assert.Null(result.CancellationReason);
        Assert.Equal(["credit_release", "stock_release"], result.CompensationPlanned);
        Assert.Equal(status, order.Status);
        Assert.Equal(0, orders.SaveChangesCallCount);

        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);

        var payload = JsonSerializer.Deserialize<CreditReleaseRequestPayload>(enqueued.Payload, JsonWire.Options)!;
        Assert.Equal(order.OrderReference.Value, payload.OrderReference);
        Assert.Equal(order.RetailerCode, payload.RetailerCode);
        Assert.Equal(order.CompanyCode, payload.CompanyCode);

        // Id 71 — the SAME synthetic envelope the stock_reserved branch
        // carries, on the credit.release enqueue site this time
        // (CancelOrderCommandHandler.cs's OTHER call to
        // OperatorCancelRequestedEnvelope.Build). This row's envelope is
        // never touched again after this INSERT — the LATER stock.release
        // row this chain owes is a separate, new row (inserted only once
        // credit.released.v1 arrives), never a rewrite of this one — so
        // this row stays the canonical note carrier for this branch.
        var triggeringFact = Assert.Single(store.EnqueuedTriggeringFacts);
        Assert.Equal(OrdersFactTopic.Name, triggeringFact.Topic);
        Assert.NotNull(triggeringFact.Envelope);
        var envelope = JsonSerializer.Deserialize<Envelope<OperatorCancelRequestedPayload>>(triggeringFact.Envelope!, JsonWire.Options)!;
        Assert.Equal("orders.cancel.requested", envelope.EventType);
        Assert.Equal(order.Id.Value, envelope.AggregateId);
        Assert.Equal(order.Id.Value, envelope.CorrelationId);
        Assert.Equal(enqueued.TriggeringEventId, envelope.EventId);
        Assert.Equal(enqueued.TriggeringEventId, envelope.CausationId);
        Assert.Equal("operator_cancelled", envelope.Payload.Reason);
        Assert.Equal(note, envelope.Payload.Note);

        var signalled = Assert.Single(signal.Signalled);
        Assert.Equal(SagaCommandKind.CreditRelease, signalled.Command);
    }

    [Fact]
    public async Task UnknownOrderId_ThrowsOrderNotFoundErrorAndTouchesNothing()
    {
        var orders = new FakeOrderRepository();
        var store = new FakeSagaCommandStore();
        var signal = new FakeSagaCommandSignal();
        var handler = BuildHandler(orders, store, signal, OrderTestData.Now);
        var unknownId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<OrderNotFoundError>(
            () => handler.HandleAsync(new CancelOrderCommand(unknownId, Note: null), CancellationToken.None));

        Assert.Equal(unknownId, error.OrderId);
        Assert.Empty(store.Enqueued);
        Assert.Empty(signal.Signalled);
    }

    private static CancelOrderCommandHandler BuildHandler(FakeOrderRepository orders, FakeSagaCommandStore store, FakeSagaCommandSignal signal, DateTimeOffset now) =>
        new(new FakeUnitOfWork(), orders, store, signal, new FakeClock(now));

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public List<(Guid OrderId, string OrderReference, SagaCommandKind Command, string Payload, Guid TriggeringEventId)> Enqueued { get; } = [];

        public List<(byte[]? Envelope, string? Topic)> EnqueuedTriggeringFacts { get; } = [];

        public EnqueueOutcome OutcomeToReturn { get; set; } = EnqueueOutcome.Enqueued;

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken)
        {
            Enqueued.Add((orderId, orderReference, command, payload, triggeringEventId));
            EnqueuedTriggeringFacts.Add((triggeringEventEnvelope, triggeringEventTopic));
            return Task.FromResult(OutcomeToReturn);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasPendingCompensationAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSagaCommandSignal : ISagaCommandSignal
    {
        public List<SagaCommandRef> Signalled { get; } = [];

        public void Signal(SagaCommandRef commandRef) => Signalled.Add(commandRef);
    }
}
