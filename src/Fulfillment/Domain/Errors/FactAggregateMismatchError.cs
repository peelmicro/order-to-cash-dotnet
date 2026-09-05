using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="StockItem.RecordOrderFact"/> when the fact's
/// <c>AggregateId</c> does not match this item's own id — the one guard that
/// stops that method being a generic "emit anything" hole (design.md §3.1).
/// </summary>
public sealed class FactAggregateMismatchError(UniqueId stockItemId, UniqueId factAggregateId)
    : DomainError("FACT_AGGREGATE_MISMATCH", $"Stock item {stockItemId} refuses a fact whose aggregateId is {factAggregateId}.")
{
    public UniqueId StockItemId { get; } = stockItemId;

    public UniqueId FactAggregateId { get; } = factAggregateId;
}
