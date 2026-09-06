using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Mirrors <c>src/Fulfillment/Domain/Errors/FactAggregateMismatchError.cs</c>
/// — reserved for the same shape of guard this aggregate's fact-recording
/// methods would need if a fact ever arrived from outside the aggregate's
/// own construction sites. <c>BuyerCredit</c> builds every one of its own
/// facts internally (`Approve`/`Refuse`/`Release`), so nothing calls this
/// today; it exists so the vocabulary is uniform across the three write
/// models if that ever changes.
/// </summary>
public sealed class FactAggregateMismatchError(UniqueId aggregateId, UniqueId factAggregateId)
    : DomainError("FACT_AGGREGATE_MISMATCH", $"Fact names aggregate '{factAggregateId}' but was recorded against '{aggregateId}'.")
{
    public UniqueId AggregateId { get; } = aggregateId;

    public UniqueId FactAggregateId { get; } = factAggregateId;
}
