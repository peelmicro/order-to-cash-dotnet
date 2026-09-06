using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// Raised by <see cref="Invoice.MarkPaid"/> — `BI14`/shared `R46`
/// (specs/shared/asyncapi.yaml <c>PaymentReceivedPayload</c>, fact 11).
/// Delivered, unit-tested and UNCALLED in this feature — feature 22's
/// `billing.payment.register` is the only production caller
/// (design.md §17).
/// </summary>
public sealed record PaymentReceived(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string InvoiceReference,
    string PaymentReference,
    Money Amount,
    DateTimeOffset ValueDate,
    string Source)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "payment.received.v1";
}
