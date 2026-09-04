using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
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

    private static SagaFactHandler BuildHandler(FakeOrderRepository orders, FakeIdempotentSagaRunner runner, FakeSagaIgnoredFactRecorder ignoredFacts, FakeSagaCommandStore store) =>
        new(orders, runner, ignoredFacts, store, Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaFactHandler>.Instance);

    private static SagaFact BuildFact(string eventType, Guid correlationId) => new(
        EventId: Guid.NewGuid(),
        EventType: eventType,
        AggregateId: correlationId,
        CorrelationId: correlationId,
        CausationId: Guid.NewGuid(),
        OccurredAt: OrderTestData.Now.AddMinutes(5),
        Payload: new object());

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

        public Task AddAsync(Order order, CancellationToken cancellationToken) => throw new NotSupportedException();

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

        public EnqueueOutcome OutcomeToReturn { get; set; } = EnqueueOutcome.Enqueued;

        public int EnqueueCalledAtSequence { get; private set; } = -1;

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken)
        {
            Enqueued.Add((orderId, orderReference, command, payload, triggeringEventId));
            EnqueueCalledAtSequence = System.Threading.Interlocked.Increment(ref _sequence);
            return Task.FromResult(OutcomeToReturn);
        }

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
