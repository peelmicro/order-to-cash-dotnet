using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// Raised by <see cref="BuyerCredit.Release"/> — `BC11`/`BC25`
/// (specs/shared/asyncapi.yaml <c>CreditReleasedPayload</c>). Never raised
/// when the order has no outstanding exposure — <see cref="BuyerCredit.Release"/>
/// returns <see langword="null"/> instead (`B5`).
/// </summary>
public sealed record CreditReleased(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    Money ReleasedAmount,
    Money AvailableCreditAfter,
    CreditReleaseReason Reason)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "credit.released.v1";
}
