using OrderToCash.Contracts.Facts;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>observability_reliability</c>, design.md §4.4 (<c>OR3</c>) —
/// <see cref="Order.RecordSagaFailure"/>: raises exactly ONE
/// <see cref="OrderSagaFailed"/>, mutates NO other field (no status, no
/// lines, no totals, no <see cref="Order.UpdatedAt"/>), bypasses
/// <c>TransitionTo</c> entirely, and both <c>correlationId</c>/<c>aggregateId</c>
/// are the order id.
/// </summary>
public sealed class OrderSagaFailureTests
{
    private static readonly UniqueId _causationId = UniqueId.New();

    /// <summary>⚑ARM — count and absence. Arm by deleting the <c>Raise</c> in <see cref="Order.RecordSagaFailure"/>: this must fail on the empty-events assertion.</summary>
    [Fact]
    public void OR3_AppendsExactlyOneOrderSagaFailedEvent_AndLeavesStatusLinesTotalsAndUpdatedAtUnchanged()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var statusBefore = order.Status;
        var linesBefore = order.Lines.ToList();
        var initialAmountBefore = order.InitialAmount;
        var initialDiscountBefore = order.InitialDiscount;
        var totalAmountBefore = order.TotalAmount;
        var updatedAtBefore = order.UpdatedAt;
        var occurredAt = OrderTestData.Now.AddMinutes(30);

        order.RecordSagaFailure("stock.reserve", 3, "no responder is subscribed.", occurredAt, _causationId);

        var raised = Assert.Single(order.DomainEvents);
        var sagaFailed = Assert.IsType<OrderSagaFailed>(raised);

        // The event-type LITERAL — a genuine substitution candidate (one of
        // fourteen *.v1 types): asserted against the exact expected string,
        // not merely "is a FactCatalog key" (which every sibling *.v1 type
        // would also satisfy).
        Assert.Equal("order.saga_failed.v1", sagaFailed.EventType);
        Assert.True(FactCatalog.PayloadTypesByEventType.ContainsKey(sagaFailed.EventType));

        Assert.Equal(order.OrderReference, sagaFailed.OrderReference);
        Assert.Equal("stock.reserve", sagaFailed.Command);
        Assert.Equal(3, sagaFailed.Attempts);
        Assert.Equal("no responder is subscribed.", sagaFailed.LastError);
        Assert.Equal(occurredAt, sagaFailed.FailedAt);
        Assert.Equal(occurredAt, sagaFailed.OccurredAt);

        // Not a T-1 transition — nothing else about the aggregate moves.
        Assert.Equal(statusBefore, order.Status);
        Assert.Equal(linesBefore.Count, order.Lines.Count);
        Assert.Equal(initialAmountBefore, order.InitialAmount);
        Assert.Equal(initialDiscountBefore, order.InitialDiscount);
        Assert.Equal(totalAmountBefore, order.TotalAmount);
        Assert.Equal(updatedAtBefore, order.UpdatedAt); // TransitionTo was never reached — UpdatedAt is untouched.
    }

    [Fact]
    public void OR3_CorrelationIdAndAggregateIdAreBothTheOrderId()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);

        order.RecordSagaFailure("despatch.create", 3, "timed out", OrderTestData.Now.AddMinutes(1), _causationId);

        var sagaFailed = Assert.IsType<OrderSagaFailed>(Assert.Single(order.DomainEvents));
        Assert.Equal(order.Id, sagaFailed.AggregateId);
        Assert.Equal(order.Id, sagaFailed.CorrelationId);
        Assert.Equal(_causationId, sagaFailed.CausationId);
    }

    /// <summary>
    /// The mapper arm must be READ THROUGH, not re-implemented — corrupting
    /// <c>OrderFactPayloadMapper.ToOrderSagaFailedPayload</c>'s field mapping
    /// (command/attempts/lastError transposed or dropped) must fail THIS
    /// test, since it drives the real mapper end to end.
    /// </summary>
    [Fact]
    public void OR3_OrderFactPayloadMapper_MapsCommandAttemptsAndLastErrorVerbatim()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.CreditApproved);
        var occurredAt = OrderTestData.Now.AddMinutes(45);
        order.RecordSagaFailure("credit.hold", 3, "INTERNAL_ERROR: boom", occurredAt, _causationId);
        var sagaFailed = Assert.IsType<OrderSagaFailed>(Assert.Single(order.DomainEvents));

        var mapper = new OrderToCash.Orders.Infrastructure.Outbox.OrderFactPayloadMapper();
        var payload = Assert.IsType<OrderToCash.Contracts.Facts.Payloads.OrderSagaFailedPayload>(mapper.ToPayload(sagaFailed));

        Assert.Equal(order.OrderReference.Value, payload.OrderReference);
        Assert.Equal("credit.hold", payload.Command);
        Assert.Equal(3, payload.Attempts);
        Assert.Equal("INTERNAL_ERROR: boom", payload.LastError);
        Assert.Equal(occurredAt, payload.FailedAt);
    }
}
