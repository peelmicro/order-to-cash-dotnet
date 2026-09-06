using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// Raised by <see cref="Invoice.Issue"/> — `BI13`/`R45` (specs/shared/asyncapi.yaml
/// <c>InvoiceIssuedPayload</c>, fact 10). <see cref="AggregateId"/> is the
/// invoice's own id, <see cref="FactEvent.CorrelationId"/> is the order id,
/// <see cref="FactEvent.CausationId"/> is the request id — `domain-model.md`
/// §7.2 names <c>Invoice</c> as this fact's producing aggregate.
/// </summary>
public sealed record InvoiceIssued(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<InvoiceLine> Lines,
    Money Amount,
    Money Discount,
    Money TotalAmount)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "invoice.issued.v1";
}
