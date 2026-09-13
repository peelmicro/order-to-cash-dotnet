using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Saga;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>observability_reliability</c>, design.md §4.4 (<c>OR3</c>) —
/// <see cref="SagaFirstParkDeadLetterHandler"/>'s OWN orchestration, over
/// fakes: the claim (<see cref="ISagaCommandStore.TryClaimDeadLetterAsync"/>),
/// the fact append (<see cref="Order.RecordSagaFailure"/> +
/// <see cref="IOrderRepository.SaveChangesAsync"/>) and the <c>.dlq</c>
/// republish (<see cref="IDeadLetterPublisher"/>) all happen INSIDE the same
/// <see cref="IUnitOfWork.ExecuteAsync{T}"/> call for the first two, and the
/// publish happens strictly AFTER it — never when the claim is lost.
/// </summary>
public sealed class SagaFirstParkDeadLetterHandlerTests
{
    private static readonly Guid _commandId = Guid.NewGuid();
    private static readonly Guid _triggeringEventId = Guid.NewGuid();

    /// <summary>
    /// ⚑ARM — absence. This is the "not called when the claim reports
    /// already-dead-lettered" case tasks.md A2e names: a later, LOSING
    /// re-park of the same row must append no second fact and publish no
    /// second <c>.dlq</c> copy. Arm by dropping the <c>if (!won)</c> guard.
    /// </summary>
    [Fact]
    public async Task OR3_AppendsNoFactAndPublishesNoDlqCopy_WhenTheClaimReportsAlreadyDeadLettered()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore { ClaimDeadLetterReturns = false };
        var deadLetters = new RecordingDeadLetterPublisher();
        var handler = BuildHandler(store, orders, deadLetters);

        var claimed = BuildClaimed(order.Id.Value, "otc.orders.facts.v1");

        await handler.HandleAsync(claimed, attempts: 3, lastError: "boom", CancellationToken.None);

        Assert.Empty(order.DomainEvents);
        Assert.Equal(0, orders.SaveChangesCallCount);
        Assert.Empty(deadLetters.Published);
        Assert.Single(store.ClaimCalls); // the claim was still ATTEMPTED — only its outcome gates everything after it.
    }

    [Fact]
    public async Task OR3_OnAWinningClaim_AppendsTheFactAndPublishesTheTriggeringEnvelopeToItsSourceTopicsDlq()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore { ClaimDeadLetterReturns = true };
        var deadLetters = new RecordingDeadLetterPublisher();
        var handler = BuildHandler(store, orders, deadLetters);

        var envelopeBytes = System.Text.Encoding.UTF8.GetBytes("""{"eventId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","eventType":"stock.rejected.v1"}""");
        var claimed = BuildClaimed(order.Id.Value, "otc.fulfillment.facts.v1", envelopeBytes);

        await handler.HandleAsync(claimed, attempts: 3, lastError: "no responder is subscribed.", CancellationToken.None);

        var sagaFailed = Assert.IsType<OrderToCash.Orders.Domain.Events.OrderSagaFailed>(Assert.Single(order.DomainEvents));
        Assert.Equal(3, sagaFailed.Attempts);
        Assert.Equal("no responder is subscribed.", sagaFailed.LastError);
        Assert.Equal("stock.reserve", sagaFailed.Command);
        Assert.Equal(1, orders.SaveChangesCallCount);

        var published = Assert.Single(deadLetters.Published);
        // The SOURCE TOPIC substitution guard — the claimed row's OWN
        // topic (fulfillment's, in this case), never a hardcoded
        // Orders-only constant. SagaFactsConsumer subscribes to three
        // topics (otc.orders/fulfillment/billing.facts.v1); a saga
        // command's triggering fact can originate from any of them.
        Assert.Equal("otc.fulfillment.facts.v1", published.SourceTopic);
        Assert.Equal(envelopeBytes, published.OriginalEnvelope.ToArray());
        Assert.Equal(ConsumerName.OrdersSaga, published.FailedConsumer);
        Assert.Equal(3, published.Attempts);
        Assert.Equal("no responder is subscribed.", published.Error);
        // The FACT's own eventType — read off the raw envelope bytes, never
        // the saga COMMAND's token ("stock.reserve").
        Assert.Equal("stock.rejected.v1", published.EventType);
    }

    /// <summary>
    /// ⚑ARM — substitution. Repointing the publish at a hardcoded
    /// <c>OrdersFactTopic.Name</c> ("otc.orders.facts.v1") instead of the
    /// claimed row's OWN <c>TriggeringEventTopic</c> would still pass every
    /// OTHER assertion in this file (both use the same literal there) — this
    /// case drives a row whose topic genuinely differs from Orders' own.
    /// </summary>
    [Fact]
    public async Task OR3_PublishesToTheClaimedRowsOwnTriggeringTopic_NeverAHardcodedOrdersTopic()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore { ClaimDeadLetterReturns = true };
        var deadLetters = new RecordingDeadLetterPublisher();
        var handler = BuildHandler(store, orders, deadLetters);

        var claimed = BuildClaimed(order.Id.Value, "otc.billing.facts.v1");

        await handler.HandleAsync(claimed, attempts: 3, lastError: "boom", CancellationToken.None);

        var published = Assert.Single(deadLetters.Published);
        Assert.Equal("otc.billing.facts.v1", published.SourceTopic);
        Assert.NotEqual("otc.orders.facts.v1", published.SourceTopic);
    }

    [Fact]
    public async Task OR3_AWinningClaimWithNoTriggeringEnvelope_AppendsTheFactButPublishesNoDlqCopy()
    {
        // A row enqueued before this feature's columns existed, or one
        // whose enqueue site genuinely supplied no envelope. This is NOT
        // an operator-cancel compensation row: since feature
        // operator_note_survives_the_compensation_branches (id 71),
        // CancelOrderCommandHandler's rows carry the synthetic
        // orders.cancel.requested envelope and ARE republished — this
        // fixture only covers the pre-feature/no-envelope case. Nothing
        // to republish here, but the fact append still happens.
        var order = OrderTestData.RehydratedOrder(OrderStatus.CreditApproved);
        var orders = new FakeOrderRepository();
        orders.Added.Add(order);
        var store = new FakeSagaCommandStore { ClaimDeadLetterReturns = true };
        var deadLetters = new RecordingDeadLetterPublisher();
        var handler = BuildHandler(store, orders, deadLetters);

        var claimed = new SagaCommandRecord(_commandId, order.Id.Value, order.OrderReference.Value, SagaCommandKind.CreditRelease, "{}", _triggeringEventId, Attempts: 0, TriggeringEventEnvelope: null, TriggeringEventTopic: null);

        await handler.HandleAsync(claimed, attempts: 3, lastError: "boom", CancellationToken.None);

        Assert.Single(order.DomainEvents);
        Assert.Equal(1, orders.SaveChangesCallCount);
        Assert.Empty(deadLetters.Published);
    }

    private static SagaFirstParkDeadLetterHandler BuildHandler(FakeSagaCommandStore store, FakeOrderRepository orders, RecordingDeadLetterPublisher deadLetters) =>
        new(store, new FakeUnitOfWork(), orders, deadLetters, new FakeClock(OrderTestData.Now.AddHours(1)), NullLogger<SagaFirstParkDeadLetterHandler>.Instance);

    private static SagaCommandRecord BuildClaimed(Guid orderId, string triggeringTopic, byte[]? envelope = null) => new(
        _commandId,
        orderId,
        "ORD-000001",
        SagaCommandKind.StockReserve,
        "{}",
        _triggeringEventId,
        Attempts: 0,
        TriggeringEventEnvelope: envelope ?? System.Text.Encoding.UTF8.GetBytes("""{"eventType":"order.placed.v1"}"""),
        TriggeringEventTopic: triggeringTopic);

    private sealed class RecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterPublication> Published { get; } = [];

        public Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken)
        {
            Published.Add(publication);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public bool ClaimDeadLetterReturns { get; set; } = true;

        public List<Guid> ClaimCalls { get; } = [];

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken)
        {
            ClaimCalls.Add(commandId);
            return Task.FromResult(ClaimDeadLetterReturns);
        }

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasAcceptedOperatorCancelAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
