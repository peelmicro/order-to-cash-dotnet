using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="StockItem.Reserve"/> when reserving <c>requested</c>
/// units would break <b>F1</b> (<c>reservedUnits ≤ units</c>) — `R30`.
/// Carries the shortage's own fields so the caller (the order-scoped domain
/// service) can build <c>stock.rejected.v1</c>'s <c>shortages[]</c> entry
/// without re-deriving them.
/// </summary>
public sealed class InsufficientStockError(string productCode, int requested, int available)
    : DomainError("INSUFFICIENT_STOCK", $"Product '{productCode}': requested {requested}, available {available}.")
{
    public string ProductCode { get; } = productCode;

    public int Requested { get; } = requested;

    public int Available { get; } = available;
}
