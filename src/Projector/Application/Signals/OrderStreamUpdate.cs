namespace OrderToCash.Projector.Application.Signals;

/// <summary>
/// <c>specs/shared/openapi.yaml</c>'s <c>OrderStreamUpdate</c> shape,
/// published on <c>readmodel.order.updated.&lt;orderId&gt;</c> — the
/// required fields are <c>[eventId, orderId, status, occurredAt]</c>.
/// Projector-owned, deliberately NOT in <c>src/Contracts</c>: that project
/// is the wire contract versioned by <c>asyncapi.yaml</c>, and this subject
/// is deliberately absent from it (design.md §7.2). Built from the
/// <b>post-apply</b> document (<c>PR42</c>) so this record can never
/// describe a state the store never held.
/// </summary>
public sealed record OrderStreamUpdate(
    Guid EventId,
    Guid OrderId,
    string? OrderReference,
    string Status,
    string? CancellationReason,
    OrderStreamReferences References,
    OrderStreamTotals? Totals,
    DateTimeOffset OccurredAt);

public sealed record OrderStreamReferences(
    string? DespatchReference,
    string? InvoiceReference,
    string? PaymentReference);

public sealed record OrderStreamTotals(
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount);
