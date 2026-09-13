using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.1 step 4, §5.3 — delegation to <see cref="SagaFactHandler"/>,
/// and the dispatch-owed event is published ONLY when the outcome is
/// <see cref="SagaFactOutcome.Processed"/> AND a command was enqueued —
/// never on duplicate, ignored, or processed-WITHOUT-enqueue (the
/// already-enqueued row case).
/// </summary>
public sealed class SagaFactCommandHandlerTests
{
    [Fact]
    public async Task ProcessedWithEnqueue_PublishesTheMatchingDispatchOwedEvent()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var (handler, dispatcher) = BuildOrderPlacedHandler(order, duplicate: false, alreadyEnqueued: false);

        await handler.HandleAsync(new HandleOrderPlacedFactCommand(BuildFact("order.placed.v1", order.Id.Value)), CancellationToken.None);

        var published = Assert.Single(dispatcher.Published);
        var @event = Assert.IsType<OrderPlacedFactRecorded>(published);
        Assert.Equal(order.Id.Value, @event.OrderId);
    }

    [Fact]
    public async Task Duplicate_PublishesNothing()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var (handler, dispatcher) = BuildOrderPlacedHandler(order, duplicate: true, alreadyEnqueued: false);

        await handler.HandleAsync(new HandleOrderPlacedFactCommand(BuildFact("order.placed.v1", order.Id.Value)), CancellationToken.None);

        Assert.Empty(dispatcher.Published);
    }

    [Fact]
    public async Task Ignored_PublishesNothing()
    {
        // order.placed.v1 requires Placed; supply an order already StockReserved.
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var (handler, dispatcher) = BuildOrderPlacedHandler(order, duplicate: false, alreadyEnqueued: false);

        await handler.HandleAsync(new HandleOrderPlacedFactCommand(BuildFact("order.placed.v1", order.Id.Value)), CancellationToken.None);

        Assert.Empty(dispatcher.Published);
    }

    [Fact]
    public async Task ProcessedWithoutEnqueue_PublishesNothing()
    {
        // The command was already owed/sent (a duplicate-key hit on enqueue)
        // — Processed, but nothing to signal (design.md §6.3).
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var (handler, dispatcher) = BuildOrderPlacedHandler(order, duplicate: false, alreadyEnqueued: true);

        await handler.HandleAsync(new HandleOrderPlacedFactCommand(BuildFact("order.placed.v1", order.Id.Value)), CancellationToken.None);

        Assert.Empty(dispatcher.Published);
    }

    [Fact]
    public async Task NonPublishingFacts_NeverPublishAnything()
    {
        // invoice.issued.v1 owes nothing (R23) — its handler never publishes.
        var order = OrderTestData.RehydratedOrder(OrderStatus.Despatched);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var store = new FakeSagaCommandStore();
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var handler = new HandleInvoiceIssuedFactCommandHandler(sagaHandler);

        await handler.HandleAsync(new HandleInvoiceIssuedFactCommand(BuildFact("invoice.issued.v1", order.Id.Value)), CancellationToken.None);

        Assert.Empty(store.Enqueued);
    }

    /// <summary>
    /// SA-4: <c>credit.released.v1</c> owes nothing on ANY variant now —
    /// its <c>credit_approved</c>/<c>confirmed</c> variant is the COMPLETING
    /// <c>Cancel</c> step (SA-4 moved the owed command to
    /// <c>stock.released.v1</c>); its <c>paid</c> variant (R24) never owed
    /// anything either. Proven by constructing the handler with NO
    /// <see cref="IDispatcher"/> at all — a plain delegation, not merely a
    /// conditional publish that happens not to fire.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Paid, "invoice_paid")]
    [InlineData(OrderStatus.CreditApproved, "order_cancelled")]
    [InlineData(OrderStatus.Confirmed, "order_cancelled")]
    public async Task CreditReleasedV1_EveryVariant_OwesNothing(OrderStatus status, string reason)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var store = new FakeSagaCommandStore();
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var handler = new HandleCreditReleasedFactCommandHandler(sagaHandler);

        var payload = new CreditReleasedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 2_450, 100_000, reason, "CR-000001");
        await handler.HandleAsync(new HandleCreditReleasedFactCommand(BuildFactWithPayload("credit.released.v1", order.Id.Value, payload)), CancellationToken.None);

        Assert.Empty(store.Enqueued);
    }

    /// <summary>
    /// SA-4: <c>stock.released.v1</c>'s <c>credit_approved</c>/<c>confirmed</c>
    /// variant now owes <c>credit.release</c> — the handler publishes
    /// <c>StockReleasedForCancellationRecorded</c>, its own dedicated event
    /// type (moved here from <c>credit.released.v1</c>, which used to own
    /// this conditional-publish shape before SA-4).
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.CreditApproved)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task StockReleasedV1_CreditApprovedOrConfirmedVariant_PublishesStockReleasedForCancellationRecorded(OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var store = new FakeSagaCommandStore();
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var dispatcher = new RecordingDispatcher();
        var handler = new HandleStockReleasedFactCommandHandler(sagaHandler, dispatcher);

        await handler.HandleAsync(new HandleStockReleasedFactCommand(BuildFact("stock.released.v1", order.Id.Value)), CancellationToken.None);

        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);

        var published = Assert.Single(dispatcher.Published);
        var @event = Assert.IsType<StockReleasedForCancellationRecorded>(published);
        Assert.Equal(order.Id.Value, @event.OrderId);
    }

    /// <summary>
    /// The ORDINARY <c>credit.approved.v1</c> path — precondition
    /// <c>StockReserved</c>, no accepted operator cancel — still publishes
    /// <c>OrderConfirmedBySaga</c>, unaffected by F1's fix: the choice is
    /// driven by what was actually enqueued (<c>DespatchCreate</c> here),
    /// not by a branch removed.
    /// </summary>
    [Fact]
    public async Task CreditApprovedV1_NormalAdvance_PublishesOrderConfirmedBySaga()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var store = new FakeSagaCommandStore { AcceptedOperatorCancelToReturn = false };
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var dispatcher = new RecordingDispatcher();
        var handler = new HandleCreditApprovedFactCommandHandler(sagaHandler, dispatcher);

        await handler.HandleAsync(new HandleCreditApprovedFactCommand(BuildFact("credit.approved.v1", order.Id.Value)), CancellationToken.None);

        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.DespatchCreate, enqueued.Command);
        var published = Assert.Single(dispatcher.Published);
        Assert.IsType<OrderConfirmedBySaga>(published);
    }

    /// <summary>
    /// Id 62 fix round 1, F1 — the LATE-approval branch (order already
    /// <c>StockReserved</c> with an accepted operator cancel) enqueues
    /// <c>CreditRelease</c>, never <c>DespatchCreate</c>, so the handler
    /// must publish <see cref="LateCreditApprovalForCancellationRecorded"/>,
    /// never <see cref="OrderConfirmedBySaga"/> — armed by reverting the
    /// late path back to always publishing <c>OrderConfirmedBySaga</c>
    /// (record's arming table, F1 guard 1).
    /// </summary>
    [Fact]
    public async Task CreditApprovedV1_LateForAnAcceptedOperatorCancel_PublishesLateCreditApprovalForCancellationRecorded_NeverOrderConfirmedBySaga()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner();
        var store = new FakeSagaCommandStore { AcceptedOperatorCancelToReturn = true };
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var dispatcher = new RecordingDispatcher();
        var handler = new HandleCreditApprovedFactCommandHandler(sagaHandler, dispatcher);

        await handler.HandleAsync(new HandleCreditApprovedFactCommand(BuildFact("credit.approved.v1", order.Id.Value)), CancellationToken.None);

        var enqueued = Assert.Single(store.Enqueued);
        Assert.Equal(SagaCommandKind.CreditRelease, enqueued.Command);

        var published = Assert.Single(dispatcher.Published);
        var @event = Assert.IsType<LateCreditApprovalForCancellationRecorded>(published);
        Assert.Equal(order.Id.Value, @event.OrderId);
        Assert.DoesNotContain(dispatcher.Published, e => e is OrderConfirmedBySaga);
    }

    private static (HandleOrderPlacedFactCommandHandler Handler, RecordingDispatcher Dispatcher) BuildOrderPlacedHandler(Order order, bool duplicate, bool alreadyEnqueued)
    {
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner { ReturnDuplicate = duplicate };
        var store = new FakeSagaCommandStore { OutcomeToReturn = alreadyEnqueued ? EnqueueOutcome.AlreadyEnqueued : EnqueueOutcome.Enqueued };
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, new FakeSagaCompletionRecorder(), new SagaCommandRequestFactory(new RpcJsonRequestSerializer()), new FakeClock(OrderTestData.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var dispatcher = new RecordingDispatcher();

        return (new HandleOrderPlacedFactCommandHandler(sagaHandler, dispatcher), dispatcher);
    }

    private static SagaFact BuildFact(string eventType, Guid correlationId) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        AggregateId: correlationId,
        CorrelationId: correlationId,
        CausationId: Guid.NewGuid(),
        OccurredAt: OrderTestData.Now.AddMinutes(5),
        Payload: new object());

    /// <summary>Same shape as <see cref="BuildFact"/>, for <c>credit.released.v1</c>'s <c>credit_approved</c>/<c>confirmed</c> <c>Cancel</c> variant, whose <c>Reason</c> delegate (<see cref="SagaStepTable.MapCreditReleaseReason"/>) casts <see cref="SagaFact.Payload"/> to <see cref="OrderToCash.Contracts.Facts.Payloads.CreditReleasedPayload"/>.</summary>
    private static SagaFact BuildFactWithPayload(string eventType, Guid correlationId, object payload) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        AggregateId: correlationId,
        CorrelationId: correlationId,
        CausationId: Guid.NewGuid(),
        OccurredAt: OrderTestData.Now.AddMinutes(5),
        Payload: payload);

    private sealed class FakeSagaCompletionRecorder : ISagaCompletionRecorder
    {
        public List<(string Outcome, TimeSpan Duration)> Recorded { get; } = [];

        public void Record(string outcome, TimeSpan duration) => Recorded.Add((outcome, duration));
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public List<object> Published { get; } = [];

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand => throw new NotSupportedException();

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult> => throw new NotSupportedException();

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult> => throw new NotSupportedException();

        public Task PublishAsync(object @event, CancellationToken cancellationToken)
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }

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

        public Task AddAsync(Order order, Guid? requestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken) => Task.FromResult(OrderToReturn);

        public Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSagaIgnoredFactRecorder : ISagaIgnoredFactRecorder
    {
        public Task RecordAsync(SagaIgnoredFactRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public List<(Guid OrderId, SagaCommandKind Command)> Enqueued { get; } = [];

        public EnqueueOutcome OutcomeToReturn { get; set; } = EnqueueOutcome.Enqueued;

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken)
        {
            Enqueued.Add((orderId, command));
            return Task.FromResult(OutcomeToReturn);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        /// <summary>Id 62/F1 — settable per test; defaults <see langword="false"/> so every EXISTING case (none of which is about the late-approval branch) is unaffected.</summary>
        public bool AcceptedOperatorCancelToReturn { get; set; }

        public Task<bool> HasAcceptedOperatorCancelAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(AcceptedOperatorCancelToReturn);
    }
}
