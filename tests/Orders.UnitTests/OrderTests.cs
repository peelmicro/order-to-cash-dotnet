using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>R5 (O1, no empty orders) and R7 (O4, lines frozen from <c>confirmed</c> onwards).</summary>
public sealed class OrderTests
{
    private static readonly UniqueId _causationId = UniqueId.New();

    /// <summary><see cref="Order.Place"/> refuses zero lines; <c>RemoveLine</c> refuses to remove the last one — both raise <c>order.must_have_at_least_one_line</c> (O1, R5).</summary>
    [Fact]
    public void R5_Order_RefusesToCreateAnOrderWithNoLinesAndToRemoveTheLastRemainingLine()
    {
        var placeError = Assert.Throws<OrderMustHaveAtLeastOneLineError>(() => Order.Place(
            orderReference: new OrderNumber(1),
            orderDate: OrderTestData.Now,
            retailerCode: OrderTestData.RetailerCode,
            buyerGln: OrderTestData.BuyerGln,
            companyCode: OrderTestData.CompanyCode,
            supplierGln: OrderTestData.SupplierGln,
            currency: OrderTestData.Currency,
            lines: [],
            notes: null,
            occurredAt: OrderTestData.Now,
            causationId: _causationId));
        Assert.Equal("order.must_have_at_least_one_line", placeError.Code);

        var order = Order.Place(
            orderReference: new OrderNumber(2),
            orderDate: OrderTestData.Now,
            retailerCode: OrderTestData.RetailerCode,
            buyerGln: OrderTestData.BuyerGln,
            companyCode: OrderTestData.CompanyCode,
            supplierGln: OrderTestData.SupplierGln,
            currency: OrderTestData.Currency,
            lines: [new OrderLineRequest("PROD-ONLY", "Only line", new Quantity(1), new Money(100, OrderTestData.Currency), Money.Zero(OrderTestData.Currency))],
            notes: null,
            occurredAt: OrderTestData.Now,
            causationId: _causationId);

        var onlyLineId = order.Lines.Single().Id;

        var removeError = Assert.Throws<OrderMustHaveAtLeastOneLineError>(() => order.RemoveLine(onlyLineId, OrderTestData.Now));
        Assert.Equal("order.must_have_at_least_one_line", removeError.Code);
        Assert.Single(order.Lines);
    }

    /// <summary>Every one of R7's six frozen statuses refuses <c>AddLine</c>, <c>RemoveLine</c> and <c>ChangeLine</c> with <c>order.lines_are_frozen</c>, leaving every field — including <c>DomainEvents.Count</c> and <c>UpdatedAt</c> — unchanged. Arming row 8: swapping the freeze and structural checks in <c>RemoveLine</c> must fail this test.</summary>
    [Fact]
    public void R7_Order_RefusesToAddRemoveOrModifyALineOnceTheOrderIsConfirmedAndLeavesEveryFieldUnchanged()
    {
        var frozenStatuses = new[]
        {
            OrderStatus.Confirmed,
            OrderStatus.Despatched,
            OrderStatus.Invoiced,
            OrderStatus.Paid,
            OrderStatus.Completed,
            OrderStatus.Cancelled,
        };

        foreach (var status in frozenStatuses)
        {
            var order = status == OrderStatus.Cancelled
                ? OrderTestData.RehydratedOrder(status, CancellationReason.OperatorCancelled)
                : OrderTestData.RehydratedOrder(status);

            var existingLineId = order.Lines[0].Id;

            AssertLinesFrozenAndUnchanged(order, () => order.AddLine("NEW-PROD", "New", new Quantity(1), new Money(100, OrderTestData.Currency), Money.Zero(OrderTestData.Currency), OrderTestData.Now));
            AssertLinesFrozenAndUnchanged(order, () => order.RemoveLine(existingLineId, OrderTestData.Now));
            AssertLinesFrozenAndUnchanged(order, () => order.ChangeLine(existingLineId, new Quantity(5), new Money(200, OrderTestData.Currency), Money.Zero(OrderTestData.Currency), OrderTestData.Now));
        }

        // The sharper case (tasks.md §7 trap 4): a single-line order where
        // removal would ALSO violate O1. Both invariants are violated; R7
        // says the answer is the frozen one — assert the Code, not merely
        // that something threw, so a guard-order regression that lets the
        // "must have at least one line" error win is caught.
        foreach (var status in frozenStatuses)
        {
            var singleLineOrder = status == OrderStatus.Cancelled
                ? OrderTestData.RehydratedOrder(status, CancellationReason.OperatorCancelled, lines: SingleLine())
                : OrderTestData.RehydratedOrder(status, lines: SingleLine());

            var onlyLineId = singleLineOrder.Lines[0].Id;

            AssertLinesFrozenAndUnchanged(singleLineOrder, () => singleLineOrder.RemoveLine(onlyLineId, OrderTestData.Now));
        }
    }

    private static IReadOnlyList<OrderLine> SingleLine() =>
        OrderTestData.PlacedOrder(lines: [new OrderLineRequest("PROD-SOLO", "Solo", new Quantity(1), new Money(100, OrderTestData.Currency), Money.Zero(OrderTestData.Currency))]).Lines;

    /// <summary>A line whose price or discount is not in the order's currency is refused with <c>order.line_currency_mismatch</c>, not the shared kernel's <c>money.cross_currency</c> (O2, design.md §5.3).</summary>
    [Fact]
    public void O2_Order_RefusesALineWhosePriceOrDiscountIsNotInTheOrdersCurrency()
    {
        var mismatchedPriceError = Assert.Throws<OrderLineCurrencyMismatchError>(() => Order.Place(
            orderReference: new OrderNumber(3),
            orderDate: OrderTestData.Now,
            retailerCode: OrderTestData.RetailerCode,
            buyerGln: OrderTestData.BuyerGln,
            companyCode: OrderTestData.CompanyCode,
            supplierGln: OrderTestData.SupplierGln,
            currency: OrderTestData.Currency,
            lines: [new OrderLineRequest("PROD-GBP", "Wrong currency price", new Quantity(1), new Money(100, "GBP"), Money.Zero(OrderTestData.Currency))],
            notes: null,
            occurredAt: OrderTestData.Now,
            causationId: _causationId));
        Assert.Equal("order.line_currency_mismatch", mismatchedPriceError.Code);

        var order = OrderTestData.PlacedOrder();

        var mismatchedDiscountError = Assert.Throws<OrderLineCurrencyMismatchError>(() => order.AddLine(
            "PROD-GBP-DISCOUNT", "Wrong currency discount", new Quantity(1), new Money(100, OrderTestData.Currency), new Money(10, "GBP"), OrderTestData.Now));
        Assert.Equal("order.line_currency_mismatch", mismatchedDiscountError.Code);
    }

    /// <summary><c>RemoveLine</c> and <c>ChangeLine</c> refuse an unknown <c>lineId</c> with <c>order.line_not_found</c> (R6, R7).</summary>
    [Fact]
    public void Order_RemoveLineAndChangeLine_RaiseOrderLineNotFoundErrorForAnUnknownLineId()
    {
        var order = OrderTestData.PlacedOrder();
        var unknownLineId = UniqueId.New();

        var removeError = Assert.Throws<OrderLineNotFoundError>(() => order.RemoveLine(unknownLineId, OrderTestData.Now));
        Assert.Equal("order.line_not_found", removeError.Code);
        Assert.Equal(unknownLineId, removeError.LineId);

        var changeError = Assert.Throws<OrderLineNotFoundError>(() => order.ChangeLine(unknownLineId, new Quantity(1), new Money(100, OrderTestData.Currency), Money.Zero(OrderTestData.Currency), OrderTestData.Now));
        Assert.Equal("order.line_not_found", changeError.Code);
        Assert.Equal(unknownLineId, changeError.LineId);
    }

    private static void AssertLinesFrozenAndUnchanged(Order order, Action attempt)
    {
        var statusBefore = order.Status;
        var lineCountBefore = order.Lines.Count;
        var totalAmountBefore = order.TotalAmount;
        var eventCountBefore = order.DomainEvents.Count;
        var updatedAtBefore = order.UpdatedAt;

        var error = Assert.Throws<OrderLinesAreFrozenError>(attempt);

        Assert.Equal("order.lines_are_frozen", error.Code);
        Assert.Equal(statusBefore, order.Status);
        Assert.Equal(lineCountBefore, order.Lines.Count);
        Assert.Equal(totalAmountBefore, order.TotalAmount);
        Assert.Equal(eventCountBefore, order.DomainEvents.Count);
        Assert.Equal(updatedAtBefore, order.UpdatedAt);
    }
}
