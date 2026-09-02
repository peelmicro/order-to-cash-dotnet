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

    /// <summary>Both halves of the O6 biconditional are refused separately, so the message says which one failed — and a status token outside the closed set (here: an <see cref="OrderStatus"/> value with no defined member) is refused too (design.md §8.3).</summary>
    [Fact]
    public void Order_Rehydrate_RefusesAStatusTokenOutsideTheClosedSetAndAReasonThatDoesNotMatchTheStatus()
    {
        var placed = OrderTestData.PlacedOrder();

        var undefinedStatusError = Assert.Throws<UnknownOrderStatusError>(() => Order.Rehydrate(
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
        Assert.Equal("order.status_unknown", undefinedStatusError.Code);

        // Half 1: status = cancelled but no reason.
        var missingReasonError = Assert.Throws<CancellationReasonRequiredError>(() => Order.Rehydrate(
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
        Assert.Equal("order.cancellation_reason_required", missingReasonError.Code);

        // Half 2: status != cancelled but a reason is present.
        var unexpectedReasonError = Assert.Throws<CancellationReasonNotApplicableError>(() => Order.Rehydrate(
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
        Assert.Equal("order.cancellation_reason_not_applicable", unexpectedReasonError.Code);
    }
}
