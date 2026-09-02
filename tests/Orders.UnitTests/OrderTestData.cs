using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// A builder, not a mock (design.md §11.1) — these tests touch no
/// infrastructure, so nothing needs a double. Supplies a valid order with
/// one retailer GLN, one supplier GLN, EUR, and two lines, so no test spends
/// its body on setup.
/// </summary>
internal static class OrderTestData
{
    public const string BuyerGlnValue = "4006381333931";
    public const string SupplierGlnValue = "5001234567890";
    public const string Currency = "EUR";
    public const string RetailerCode = "RETAILER-01";
    public const string CompanyCode = "COMPANY-01";

    public static readonly GLN BuyerGln = new(BuyerGlnValue);
    public static readonly GLN SupplierGln = new(SupplierGlnValue);
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Two lines: 1000 minor units × 2 with 50 discount, and 500 minor units × 1 with 0 discount — initialAmount 2500, initialDiscount 50, totalAmount 2450.</summary>
    public static IReadOnlyList<OrderLineRequest> TwoLines() =>
    [
        new OrderLineRequest("PROD-001", "First product", new Quantity(2), new Money(1_000, Currency), new Money(50, Currency)),
        new OrderLineRequest("PROD-002", "Second product", new Quantity(1), new Money(500, Currency), Money.Zero(Currency)),
    ];

    public static Order PlacedOrder(DateTimeOffset? occurredAt = null, UniqueId? causationId = null, IReadOnlyList<OrderLineRequest>? lines = null, string? notes = null) =>
        Order.Place(
            orderReference: new OrderNumber(1),
            orderDate: occurredAt ?? Now,
            retailerCode: RetailerCode,
            buyerGln: BuyerGln,
            companyCode: CompanyCode,
            supplierGln: SupplierGln,
            currency: Currency,
            lines: lines ?? TwoLines(),
            notes: notes,
            occurredAt: occurredAt ?? Now,
            causationId: causationId ?? UniqueId.New());

    /// <summary>Rehydrates an order directly into <paramref name="status"/>, bypassing every legal-walk restriction — the R9 test's way of constructing an order "in an arbitrary from state" (design.md §3.4).</summary>
    public static Order RehydratedOrder(
        OrderStatus status,
        CancellationReason? cancellationReason = null,
        DateTimeOffset? updatedAt = null,
        IReadOnlyList<OrderLine>? lines = null)
    {
        var order = PlacedOrder();
        var sourceLines = lines ?? order.Lines;

        return Order.Rehydrate(
            id: order.Id,
            orderReference: order.OrderReference,
            orderDate: order.OrderDate,
            retailerCode: order.RetailerCode,
            buyerGln: order.BuyerGln,
            companyCode: order.CompanyCode,
            supplierGln: order.SupplierGln,
            currency: order.Currency,
            status: status,
            cancellationReason: cancellationReason,
            notes: order.Notes,
            lines: sourceLines,
            createdAt: order.CreatedAt,
            updatedAt: updatedAt ?? order.CreatedAt);
    }
}
