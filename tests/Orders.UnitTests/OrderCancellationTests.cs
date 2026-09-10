using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>R10 (O6) — cancellation carries an immutable reason from the closed set.</summary>
public sealed class OrderCancellationTests
{
    private static readonly UniqueId _causationId = UniqueId.New();

    /// <summary>The reason lands on the aggregate, a second <c>Cancel</c> is refused (immutability via terminality), and <c>OrderCancelled</c> carries the reason and the compensation steps — empty for <c>stock_rejected</c> (R26). Arming row 4: deleting the <c>Raise</c> in <c>Cancel</c> must fail this test.</summary>
    [Fact]
    public void R10_Order_RequiresAReasonFromTheClosedSetRecordsItImmutablyAndCarriesItOnOrderCancelledV1()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);

        order.Cancel(CancellationReason.StockRejected, [], OrderTestData.Now, _causationId);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.StockRejected, order.CancellationReason);

        var raised = Assert.Single(order.DomainEvents);
        var cancelled = Assert.IsType<OrderCancelled>(raised);
        Assert.Equal("order.cancelled.v1", cancelled.EventType);
        Assert.Equal(CancellationReason.StockRejected, cancelled.CancellationReason);
        Assert.Empty(cancelled.CompensationSteps);

        // A second Cancel is refused — immutability via terminality, not a guard on the reason field itself.
        Assert.Throws<OrderNotCancellableError>(() => order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId));
        Assert.Equal(CancellationReason.StockRejected, order.CancellationReason);

        // credit_rejected from stock_reserved carries non-empty compensation steps.
        var withCompensation = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var steps = new[]
        {
            new OrderCompensationStep(CompensationStepKind.StockReleased, UniqueId.New(), "stock.released.v1", OrderTestData.Now, "Reservation returned to available stock."),
        };

        withCompensation.Cancel(CancellationReason.CreditRejected, steps, OrderTestData.Now, _causationId);

        var withCompensationEvent = Assert.IsType<OrderCancelled>(Assert.Single(withCompensation.DomainEvents));
        Assert.Equal(steps, withCompensationEvent.CompensationSteps);

        Assert.Equal("stock_released", CompensationStepKinds.ToToken(CompensationStepKind.StockReleased));
        Assert.Equal("credit_released", CompensationStepKinds.ToToken(CompensationStepKind.CreditReleased));
    }

    /// <summary>"IF no reason is supplied" is reachable only at the parse boundary (an enum parameter cannot be absent) — the status is unchanged when the parse itself refuses, before <c>Cancel</c> is ever called (design.md §6.2).</summary>
    [Fact]
    public void R10_Order_RaisesWhenNoCancellationReasonIsSuppliedAndDoesNotChangeTheStatus()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var statusBefore = order.Status;

        Assert.Throws<CancellationReasonRequiredError>(() =>
        {
            var parsedReason = CancellationReasons.Parse(null);
            order.Cancel(parsedReason, [], OrderTestData.Now, _causationId);
        });

        Assert.Equal(statusBefore, order.Status);
        Assert.Null(order.CancellationReason);
        Assert.Empty(order.DomainEvents);
    }

    /// <summary><c>stock_rejected</c> only from <c>placed</c>, <c>credit_rejected</c> only from <c>stock_reserved</c>, <c>operator_cancelled</c> from all four cancellable states — Table T-1's <em>Trigger</em> column (design.md §6.1, #7's OA4). Arming row 10: deleting the <c>credit_rejected</c> guard must fail this test.</summary>
    [Fact]
    public void R10_Order_RefusesACancellationReasonTableT1DoesNotPairWithTheCurrentStatus()
    {
        // The two paired legal cases succeed — asserted here directly so this
        // test alone catches a deleted pairing guard, not only the wrong-status refusals below.
        var stockRejectedFromPlaced = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        stockRejectedFromPlaced.Cancel(CancellationReason.StockRejected, [], OrderTestData.Now, _causationId);
        Assert.Equal(OrderStatus.Cancelled, stockRejectedFromPlaced.Status);

        var creditRejectedFromStockReserved = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        creditRejectedFromStockReserved.Cancel(CancellationReason.CreditRejected, [], OrderTestData.Now, _causationId);
        Assert.Equal(OrderStatus.Cancelled, creditRejectedFromStockReserved.Status);

        var stockRejectedFromWrongStatuses = new[] { OrderStatus.StockReserved, OrderStatus.CreditApproved, OrderStatus.Confirmed };
        foreach (var status in stockRejectedFromWrongStatuses)
        {
            var order = OrderTestData.RehydratedOrder(status);

            var error = Assert.Throws<CancellationReasonNotApplicableError>(() => order.Cancel(CancellationReason.StockRejected, [], OrderTestData.Now, _causationId));

            Assert.Equal("order.cancellation_reason_not_applicable", error.Code);
            Assert.Equal(status, order.Status);
        }

        var creditRejectedFromWrongStatuses = new[] { OrderStatus.Placed, OrderStatus.CreditApproved, OrderStatus.Confirmed };
        foreach (var status in creditRejectedFromWrongStatuses)
        {
            var order = OrderTestData.RehydratedOrder(status);

            var error = Assert.Throws<CancellationReasonNotApplicableError>(() => order.Cancel(CancellationReason.CreditRejected, [], OrderTestData.Now, _causationId));

            Assert.Equal("order.cancellation_reason_not_applicable", error.Code);
            Assert.Equal(status, order.Status);
        }

        var operatorCancelledFromAllFour = new[] { OrderStatus.Placed, OrderStatus.StockReserved, OrderStatus.CreditApproved, OrderStatus.Confirmed };
        foreach (var status in operatorCancelledFromAllFour)
        {
            var order = OrderTestData.RehydratedOrder(status);

            order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId);

            Assert.Equal(OrderStatus.Cancelled, order.Status);
        }
    }

    /// <summary>
    /// SA-2/feature <c>operator_note_reaches_the_timeline</c> bullet 2's
    /// domain half: <see cref="Order.Cancel"/>'s <c>note</c> parameter is
    /// optional, and a call that omits it (every fact-driven caller,
    /// <c>stock_rejected</c>/<c>credit_rejected</c> included) raises
    /// <see cref="OrderCancelled"/> with <see cref="OrderCancelled.Note"/>
    /// absent (<see langword="null"/>) rather than an empty string — the
    /// distinction the wire's own optionality depends on.
    /// </summary>
    [Fact]
    public void SA2_Cancel_WithNoNoteSupplied_RaisesOrderCancelledWithNoteAbsent()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);

        order.Cancel(CancellationReason.StockRejected, [], OrderTestData.Now, _causationId);

        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(order.DomainEvents));
        Assert.Null(cancelled.Note);
    }

    /// <summary>
    /// SA-2/feature <c>operator_note_reaches_the_timeline</c> bullet 1's
    /// domain half: the exact string supplied to <c>note</c> is carried
    /// verbatim onto <see cref="OrderCancelled.Note"/> — bracketed to a
    /// value the test itself supplies (CLAUDE.md's provenance rule), so a
    /// corruption of the value in transit, not merely its presence, would
    /// fail this assertion.
    /// </summary>
    [Fact]
    public void SA2_Cancel_WithANoteSupplied_RaisesOrderCancelledCarryingTheExactNoteText()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        const string note = "Buyer requested cancellation by phone — ref #A1B2C3.";

        order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId, note: note);

        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(order.DomainEvents));
        Assert.Equal(note, cancelled.Note);
    }
}
