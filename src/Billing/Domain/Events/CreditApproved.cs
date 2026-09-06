using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// Raised by <see cref="BuyerCredit.Approve"/> — `BC10` (specs/shared/asyncapi.yaml
/// <c>CreditApprovedPayload</c>). <see cref="AvailableCreditAfter"/> is
/// recomputed from the ledger INCLUDING the entry just appended — never the
/// pre-hold value, never a value carried from the request.
/// </summary>
public sealed record CreditApproved(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    Money HeldAmount,
    Money AvailableCreditAfter)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "credit.approved.v1";
}
