using OrderToCash.Contracts.Facts;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Design guards for O8 (design.md §7.4, §11.3) — no matrix row of their
/// own, but every one of them is a fact-emission-or-suppression branch
/// CLAUDE.md requires a test that fails when the emission is deleted or
/// added. This whole feature is the "double force" case: no Gateway, no
/// responder, no saga and no outbox exist yet, so these unit tests are the
/// only thing that will ever execute these branches before feature 16.
/// </summary>
public sealed class OrderEventsTests
{
    private static readonly UniqueId _causationId = UniqueId.New();

    /// <summary>The four fact-bearing edges each append exactly one event. Arming rows 1, 2, 3: deleting the <c>Raise</c> in <c>Place</c>, <c>Confirm</c> or <c>Complete</c> must fail this test.</summary>
    [Fact]
    public void O8_Order_AppendsExactlyOneDomainEventForEachFactBearingEdgeOfTableT1()
    {
        var placed = OrderTestData.PlacedOrder(causationId: _causationId);
        var placedEvent = Assert.IsType<OrderPlaced>(Assert.Single(placed.DomainEvents));
        Assert.Equal("order.placed.v1", placedEvent.EventType);

        var confirmable = OrderTestData.RehydratedOrder(OrderStatus.CreditApproved);
        confirmable.Confirm(OrderTestData.Now, _causationId);
        var confirmedEvent = Assert.IsType<OrderConfirmed>(Assert.Single(confirmable.DomainEvents));
        Assert.Equal("order.confirmed.v1", confirmedEvent.EventType);

        var completable = OrderTestData.RehydratedOrder(OrderStatus.Paid);
        completable.Complete(OrderTestData.Now, _causationId);
        var completedEvent = Assert.IsType<OrderCompleted>(Assert.Single(completable.DomainEvents));
        Assert.Equal("order.completed.v1", completedEvent.EventType);

        var cancellable = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        cancellable.Cancel(CancellationReason.StockRejected, [], OrderTestData.Now, _causationId);
        var cancelledEvent = Assert.IsType<OrderCancelled>(Assert.Single(cancellable.DomainEvents));
        Assert.Equal("order.cancelled.v1", cancelledEvent.EventType);
    }

    /// <summary>
    /// The five silent edges — <c>stock_reserved</c>, <c>credit_approved</c>,
    /// <c>despatched</c>, <c>invoiced</c>, <c>paid</c> — append no event
    /// (design.md §7.4). This is a suppression guard, not an emission one:
    /// arming row 5 fails when an emission is <em>added</em> to one of these
    /// edges, not when one is deleted.
    /// </summary>
    [Fact]
    public void O8_Order_AppendsNoDomainEventOnTheFiveSilentEdgesOfTableT1()
    {
        var stockReservable = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        stockReservable.MarkStockReserved(OrderTestData.Now);
        Assert.Empty(stockReservable.DomainEvents);

        var creditApprovable = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        creditApprovable.ApproveCredit(OrderTestData.Now);
        Assert.Empty(creditApprovable.DomainEvents);

        var despatchable = OrderTestData.RehydratedOrder(OrderStatus.Confirmed);
        despatchable.MarkDespatched(OrderTestData.Now);
        Assert.Empty(despatchable.DomainEvents);

        var invoiceable = OrderTestData.RehydratedOrder(OrderStatus.Despatched);
        invoiceable.MarkInvoiced(OrderTestData.Now);
        Assert.Empty(invoiceable.DomainEvents);

        var payable = OrderTestData.RehydratedOrder(OrderStatus.Invoiced);
        payable.MarkPaid(OrderTestData.Now);
        Assert.Empty(payable.DomainEvents);
    }

    /// <summary>Two events from one order carry different <c>EventId</c>s and the same <c>CorrelationId</c> (the order id) — preparing R12, which feature 14 finishes on the outbox side.</summary>
    [Fact]
    public void R12_Order_StampsEveryDomainEventWithAFreshEventIdTheOrderIdAsCorrelationIdAndTheSuppliedCausationId()
    {
        var causationIdForPlace = UniqueId.New();
        var causationIdForConfirm = UniqueId.New();

        var order = OrderTestData.PlacedOrder(causationId: causationIdForPlace);
        order.MarkStockReserved(OrderTestData.Now);
        order.ApproveCredit(OrderTestData.Now);
        order.Confirm(OrderTestData.Now, causationIdForConfirm);

        Assert.Equal(2, order.DomainEvents.Count);

        var placedEvent = Assert.IsType<OrderPlaced>(order.DomainEvents[0]);
        var confirmedEvent = Assert.IsType<OrderConfirmed>(order.DomainEvents[1]);

        Assert.NotEqual(placedEvent.EventId, confirmedEvent.EventId);
        Assert.Equal(order.Id, placedEvent.CorrelationId);
        Assert.Equal(order.Id, confirmedEvent.CorrelationId);
        Assert.Equal(order.Id, placedEvent.AggregateId);
        Assert.Equal(order.Id, confirmedEvent.AggregateId);
        Assert.Equal(causationIdForPlace, placedEvent.CausationId);
        Assert.Equal(causationIdForConfirm, confirmedEvent.CausationId);
    }

    /// <summary>Every <c>EventType</c> this aggregate raises is a key of <see cref="FactCatalog.PayloadTypesByEventType"/> — catches a typo in an <c>EventType</c> literal at unit-test time rather than at the first Kafka publish (design.md §11.4). This test project is allowed to reference Contracts; <c>Domain/</c> is not (§11.2).</summary>
    [Fact]
    public void Order_EventTypes_AreAllDeclaredInTheSharedFactCatalog()
    {
        var eventTypes = new[]
        {
            new OrderPlaced(UniqueId.New(), UniqueId.New(), UniqueId.New(), UniqueId.New(), OrderTestData.Now, new OrderNumber(1), "R", "C", OrderTestData.BuyerGln, OrderTestData.SupplierGln, "EUR", OrderTestData.Now, [], Money.Zero("EUR"), Money.Zero("EUR"), Money.Zero("EUR"), null).EventType,
            new OrderConfirmed(UniqueId.New(), UniqueId.New(), UniqueId.New(), UniqueId.New(), OrderTestData.Now, new OrderNumber(1), "R", "C", "EUR", Money.Zero("EUR"), OrderTestData.Now).EventType,
            new OrderCompleted(UniqueId.New(), UniqueId.New(), UniqueId.New(), UniqueId.New(), OrderTestData.Now, new OrderNumber(1), "R", "C", "EUR", Money.Zero("EUR"), OrderTestData.Now).EventType,
            new OrderCancelled(UniqueId.New(), UniqueId.New(), UniqueId.New(), UniqueId.New(), OrderTestData.Now, new OrderNumber(1), "R", "C", CancellationReason.OperatorCancelled, OrderTestData.Now, []).EventType,
        };

        foreach (var eventType in eventTypes)
        {
            Assert.True(FactCatalog.PayloadTypesByEventType.ContainsKey(eventType), $"'{eventType}' is not a key of FactCatalog.PayloadTypesByEventType.");
        }
    }

    /// <summary>The drain contract feature 14 depends on: <c>ClearDomainEvents</c> empties the pending list and leaves every other field untouched.</summary>
    [Fact]
    public void Order_ClearDomainEvents_EmptiesThePendingListAndLeavesEveryOtherFieldUntouched()
    {
        var order = OrderTestData.PlacedOrder();
        Assert.NotEmpty(order.DomainEvents);

        var statusBefore = order.Status;
        var totalAmountBefore = order.TotalAmount;
        var lineCountBefore = order.Lines.Count;
        var updatedAtBefore = order.UpdatedAt;

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
        Assert.Equal(statusBefore, order.Status);
        Assert.Equal(totalAmountBefore, order.TotalAmount);
        Assert.Equal(lineCountBefore, order.Lines.Count);
        Assert.Equal(updatedAtBefore, order.UpdatedAt);
    }
}
