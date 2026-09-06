using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// Raised by <see cref="BuyerCredit.Refuse"/> — the ONE builder, the ONE
/// call site, for every refusal (`BC14`, `R44`, design.md §3.4): a genuine
/// <c>over_limit</c> refusal and an adapter refusal are produced by the
/// SAME lines of code and differ in exactly one field, <see cref="Reason"/>.
/// (specs/shared/asyncapi.yaml <c>CreditRejectedPayload</c>.)
/// </summary>
public sealed record CreditRejected(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    Money RequestedAmount,
    Money AvailableCredit,
    CreditRejectionReason Reason)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "credit.rejected.v1";
}
