using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>R6 (O3) — totals are recomputed on every line mutation and may not be negative.</summary>
public sealed class OrderTotalsTests
{
    /// <summary>
    /// initialAmount = Σ(unitPrice × quantity); initialDiscount =
    /// Σ(lineDiscount) + orderDiscount, and orderDiscount is always
    /// <c>Money.Zero</c> here (design.md §4.4), so initialDiscount is simply
    /// the sum of line discounts; totalAmount = initialAmount −
    /// initialDiscount. Asserted after add, remove and change.
    /// </summary>
    [Fact]
    public void R6_Order_RecomputesInitialAmountInitialDiscountAndTotalAmountAfterEachMutation()
    {
        var currency = OrderTestData.Currency;
        var order = Order.Place(
            orderReference: new OrderNumber(10),
            orderDate: OrderTestData.Now,
            retailerCode: OrderTestData.RetailerCode,
            buyerGln: OrderTestData.BuyerGln,
            companyCode: OrderTestData.CompanyCode,
            supplierGln: OrderTestData.SupplierGln,
            currency: currency,
            lines: [new OrderLineRequest("PROD-A", "A", new Quantity(2), new Money(1_000, currency), new Money(100, currency))],
            notes: null,
            occurredAt: OrderTestData.Now,
            causationId: UniqueId.New());

        // Placed: 2*1000 = 2000 initialAmount, 100 initialDiscount, 1900 total.
        Assert.Equal(new Money(2_000, currency), order.InitialAmount);
        Assert.Equal(new Money(100, currency), order.InitialDiscount);
        Assert.Equal(new Money(1_900, currency), order.TotalAmount);

        // Add: + 3*500 = 1500 amount, +50 discount.
        var addedLineId = order.AddLine("PROD-B", "B", new Quantity(3), new Money(500, currency), new Money(50, currency), OrderTestData.Now);
        Assert.Equal(new Money(3_500, currency), order.InitialAmount);
        Assert.Equal(new Money(150, currency), order.InitialDiscount);
        Assert.Equal(new Money(3_350, currency), order.TotalAmount);

        // Change: PROD-B becomes 1*200 with 0 discount.
        order.ChangeLine(addedLineId, new Quantity(1), new Money(200, currency), Money.Zero(currency), OrderTestData.Now);
        Assert.Equal(new Money(2_200, currency), order.InitialAmount);
        Assert.Equal(new Money(100, currency), order.InitialDiscount);
        Assert.Equal(new Money(2_100, currency), order.TotalAmount);

        // Remove: back to just PROD-A.
        order.RemoveLine(addedLineId, OrderTestData.Now);
        Assert.Equal(new Money(2_000, currency), order.InitialAmount);
        Assert.Equal(new Money(100, currency), order.InitialDiscount);
        Assert.Equal(new Money(1_900, currency), order.TotalAmount);
    }

    /// <summary>A mutation whose candidate total would be negative is refused with <c>order.total_must_not_be_negative</c>, and every field of the aggregate — including <c>DomainEvents.Count</c> and <c>UpdatedAt</c> — is left exactly as it was (design.md §4.3, tasks.md §7 trap 5).</summary>
    [Fact]
    public void R6_Order_RejectsAMutationWhoseResultingTotalAmountWouldBeNegativeAndLeavesTheOrderUnchanged()
    {
        var currency = OrderTestData.Currency;
        var order = Order.Place(
            orderReference: new OrderNumber(11),
            orderDate: OrderTestData.Now,
            retailerCode: OrderTestData.RetailerCode,
            buyerGln: OrderTestData.BuyerGln,
            companyCode: OrderTestData.CompanyCode,
            supplierGln: OrderTestData.SupplierGln,
            currency: currency,
            lines: [new OrderLineRequest("PROD-A", "A", new Quantity(1), new Money(100, currency), Money.Zero(currency))],
            notes: null,
            occurredAt: OrderTestData.Now,
            causationId: UniqueId.New());

        var statusBefore = order.Status;
        var initialAmountBefore = order.InitialAmount;
        var initialDiscountBefore = order.InitialDiscount;
        var totalAmountBefore = order.TotalAmount;
        var lineCountBefore = order.Lines.Count;
        var eventCountBefore = order.DomainEvents.Count;
        var updatedAtBefore = order.UpdatedAt;

        // A discount larger than the price would make totalAmount negative.
        var error = Assert.Throws<OrderTotalMustNotBeNegativeError>(() => order.AddLine(
            "PROD-B", "B", new Quantity(1), new Money(50, currency), new Money(500, currency), OrderTestData.Now));

        Assert.Equal("order.total_must_not_be_negative", error.Code);
        Assert.Equal(statusBefore, order.Status);
        Assert.Equal(initialAmountBefore, order.InitialAmount);
        Assert.Equal(initialDiscountBefore, order.InitialDiscount);
        Assert.Equal(totalAmountBefore, order.TotalAmount);
        Assert.Equal(lineCountBefore, order.Lines.Count);
        Assert.Equal(eventCountBefore, order.DomainEvents.Count);
        Assert.Equal(updatedAtBefore, order.UpdatedAt);
    }
}
