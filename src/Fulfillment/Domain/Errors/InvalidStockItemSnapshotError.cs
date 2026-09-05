using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="StockItem.Reconstitute"/> when a persisted row fails
/// a structural check a legal walk through the aggregate could never produce
/// — a negative counter, <c>reservedUnits &gt; units</c> (F1), or a
/// reservation set whose reserved units do not fit an <c>int</c> (`FS20`). A
/// load-time fault, not a business rejection of a live request — the same
/// distinction <c>Order</c>'s own <c>InvalidOrderSnapshotError</c> draws.
/// </summary>
public sealed class InvalidStockItemSnapshotError(UniqueId? stockItemId, string reason)
    : DomainError("INVALID_STOCK_ITEM_SNAPSHOT", stockItemId is { } id ? $"Stock item {id}: invalid snapshot: {reason}" : $"Invalid snapshot: {reason}")
{
    public UniqueId? StockItemId { get; } = stockItemId;

    public string Reason { get; } = reason;
}
