using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary><c>Order.Rehydrate</c> (design.md §8.3) — no matrix row of its own, but the design guards this feature's persistence contract rests on.</summary>
public sealed class OrderRehydrationTests
{
    /// <summary>Restores a terminal order — <c>completed</c> and <c>cancelled</c>, the two states the seed actually contains — without walking the state machine and without raising any event. Arming row 7: adding a <c>Raise</c> to <c>Rehydrate</c> must fail this test.</summary>
    [Fact]
    public void Order_Rehydrate_RestoresATerminalOrderWithoutWalkingTheStateMachineAndWithoutRaisingAnyEvent()
    {
        var completed = OrderTestData.RehydratedOrder(OrderStatus.Completed);
        Assert.Equal(OrderStatus.Completed, completed.Status);
        Assert.Empty(completed.DomainEvents);

        var cancelled = OrderTestData.RehydratedOrder(OrderStatus.Cancelled, CancellationReason.OperatorCancelled);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(CancellationReason.OperatorCancelled, cancelled.CancellationReason);
        Assert.Empty(cancelled.DomainEvents);
    }

    /// <summary><c>Rehydrate</c> takes no totals parameters (design.md §8.3): the same lines rehydrated give the totals <c>Place</c> would have computed, so a stored/derived drift is unrepresentable rather than merely detected (#7's OA3).</summary>
    [Fact]
    public void Order_Rehydrate_DerivesTheThreeTotalsFromTheLinesRatherThanFromStoredValues()
    {
        var placed = OrderTestData.PlacedOrder();

        var rehydrated = Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: placed.Currency,
            status: OrderStatus.Placed,
            cancellationReason: null,
            notes: placed.Notes,
            lines: placed.Lines,
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt);

        Assert.Equal(placed.InitialAmount, rehydrated.InitialAmount);
        Assert.Equal(placed.InitialDiscount, rehydrated.InitialDiscount);
        Assert.Equal(placed.TotalAmount, rehydrated.TotalAmount);
    }

    /// <summary>
    /// Both halves of the O6 biconditional are refused separately, so the
    /// message says which one failed — and a status token outside the
    /// closed set (here: an <see cref="OrderStatus"/> value with no defined
    /// member) is refused too (design.md §8.3). All three raise
    /// <see cref="InvalidOrderSnapshotError"/> (<c>order.snapshot_invalid</c>)
    /// rather than the live-request errors the pre-follow-up code reused —
    /// orders_acceptance follow-up 3, closing review_orders_aggregate.md's
    /// advisory A3: a corrupt stored row is a load-time fault, not a
    /// business rejection of a caller's request.
    /// </summary>
    [Fact]
    public void Order_Rehydrate_RefusesAStatusTokenOutsideTheClosedSetAndAReasonThatDoesNotMatchTheStatus()
    {
        var placed = OrderTestData.PlacedOrder();

        var undefinedStatusError = Assert.Throws<InvalidOrderSnapshotError>(() => Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: placed.Currency,
            status: (OrderStatus)99,
            cancellationReason: null,
            notes: placed.Notes,
            lines: placed.Lines,
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt));
        Assert.Equal("order.snapshot_invalid", undefinedStatusError.Code);
        Assert.Equal(placed.Id, undefinedStatusError.OrderId);

        // Half 1: status = cancelled but no reason.
        var missingReasonError = Assert.Throws<InvalidOrderSnapshotError>(() => Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: placed.Currency,
            status: OrderStatus.Cancelled,
            cancellationReason: null,
            notes: placed.Notes,
            lines: placed.Lines,
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt));
        Assert.Equal("order.snapshot_invalid", missingReasonError.Code);

        // Half 2: status != cancelled but a reason is present.
        var unexpectedReasonError = Assert.Throws<InvalidOrderSnapshotError>(() => Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: placed.Currency,
            status: OrderStatus.Placed,
            cancellationReason: CancellationReason.OperatorCancelled,
            notes: placed.Notes,
            lines: placed.Lines,
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt));
        Assert.Equal("order.snapshot_invalid", unexpectedReasonError.Code);
    }

    /// <summary>
    /// O1 on the load path: an empty <c>lines</c> collection is refused with
    /// the SAME error <c>Place</c>/<c>RemoveLine</c> raise (matching #7's own
    /// <c>reconstitute</c>, which reuses its live <c>EmptyOrderError</c> here
    /// too — design.md §8.3's first of two design-review follow-ups closed by
    /// orders_acceptance). Arming: deleting the <c>lines.Count == 0</c> check
    /// in <c>Rehydrate</c> must fail this test (review_orders_aggregate.md D1).
    /// </summary>
    [Fact]
    public void Order_Rehydrate_RefusesAnEmptyLinesCollection()
    {
        var placed = OrderTestData.PlacedOrder();

        var error = Assert.Throws<OrderMustHaveAtLeastOneLineError>(() => Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: placed.Currency,
            status: OrderStatus.Placed,
            cancellationReason: null,
            notes: placed.Notes,
            lines: [],
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt));
        Assert.Equal("order.must_have_at_least_one_line", error.Code);
    }

    /// <summary>
    /// O2 on the load path: a line whose money is not in the order's
    /// currency is refused with the aggregate's OWN
    /// <see cref="OrderLineCurrencyMismatchError"/> (<c>order.line_currency_mismatch</c>)
    /// rather than the shared kernel's <c>money.cross_currency</c> — the
    /// exact check #7's own reviewer flagged as missing (its defect D3) and
    /// design.md §8.3 requires here. Arming: deleting the
    /// <c>EnsureLineCurrencyMatches</c> loop in <c>Rehydrate</c> must fail
    /// this test (review_orders_aggregate.md D1, second of two follow-ups
    /// closed by orders_acceptance).
    /// </summary>
    [Fact]
    public void Order_Rehydrate_RefusesALineWhoseMoneyIsNotInTheOrdersCurrency()
    {
        // The row's own lines are genuine EUR Money (OrderLine's constructor
        // is internal — only Orders.csproj may mint one, so the corruption
        // is simulated the other way: declaring a "row currency" the lines
        // do not match, exactly the shape a stored `currency_id` pointing at
        // the wrong reference row would produce).
        var placed = OrderTestData.PlacedOrder();

        var error = Assert.Throws<OrderLineCurrencyMismatchError>(() => Order.Rehydrate(
            id: placed.Id,
            orderReference: placed.OrderReference,
            orderDate: placed.OrderDate,
            retailerCode: placed.RetailerCode,
            buyerGln: placed.BuyerGln,
            companyCode: placed.CompanyCode,
            supplierGln: placed.SupplierGln,
            currency: "USD",
            status: OrderStatus.Placed,
            cancellationReason: null,
            notes: placed.Notes,
            lines: placed.Lines,
            createdAt: placed.CreatedAt,
            updatedAt: placed.UpdatedAt));
        Assert.Equal("order.line_currency_mismatch", error.Code);
        Assert.NotEqual("money.cross_currency", error.Code);
    }
}
