using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
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
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
        var handler = new HandleInvoiceIssuedFactCommandHandler(sagaHandler);

        await handler.HandleAsync(new HandleInvoiceIssuedFactCommand(BuildFact("invoice.issued.v1", order.Id.Value)), CancellationToken.None);

        Assert.Empty(store.Enqueued);
    }

    private static (HandleOrderPlacedFactCommandHandler Handler, RecordingDispatcher Dispatcher) BuildOrderPlacedHandler(Order order, bool duplicate, bool alreadyEnqueued)
    {
        var orders = new FakeOrderRepository { OrderToReturn = order };
        var runner = new FakeIdempotentSagaRunner { ReturnDuplicate = duplicate };
        var store = new FakeSagaCommandStore { OutcomeToReturn = alreadyEnqueued ? EnqueueOutcome.AlreadyEnqueued : EnqueueOutcome.Enqueued };
        var sagaHandler = new SagaFactHandler(orders, runner, new FakeSagaIgnoredFactRecorder(), store, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);
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

        public Task AddAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken)
        {
            Enqueued.Add((orderId, command));
            return Task.FromResult(OutcomeToReturn);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
