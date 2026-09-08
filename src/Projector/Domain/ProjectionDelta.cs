namespace OrderToCash.Projector.Domain;

/// <summary>One timeline entry to append — the seed's <c>TimelineEvent</c> declaration order, <c>causationId</c> last (design.md §3.2).</summary>
public sealed record TimelineEntryDelta(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyDictionary<string, object>? Detail,
    Guid CausationId); // PR30 — verbatim from the envelope, never eventType-derived.

/// <summary>One order line of the header (<c>order.placed.v1</c> only).</summary>
public sealed record OrderItemDelta(
    string ProductCode,
    int Quantity,
    long UnitPrice,
    long LineDiscount);

/// <summary>The header fields <c>order.placed.v1</c> alone fills, unconditionally (PR9).</summary>
public sealed record OrderHeaderDelta(
    string OrderReference,
    DateTimeOffset OrderDate,
    string RetailerCode,
    string BuyerGln,
    string CompanyCode,
    string SupplierGln,
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    IReadOnlyList<OrderItemDelta> Items);

/// <summary>
/// A store-agnostic description of one fact's effect on the read model —
/// Infrastructure alone translates this into <c>PR6</c>'s pipeline
/// (<c>DeltaToPipeline</c>). Every field a fact does not touch is
/// <see langword="null"/>.
/// </summary>
public sealed record ProjectionDelta(
    Guid OrderId, // = CorrelationId
    TimelineEntryDelta Entry,
    string? ImpliedStatus, // null for the five status-less facts
    int StatusRank, // 0 when ImpliedStatus is null
    string? CancellationReasonIfAbsent,
    string? DespatchReferenceIfAbsent,
    string? InvoiceReferenceIfAbsent,
    string? PaymentReferenceIfAbsent,
    OrderHeaderDelta? Header); // present ONLY for order.placed.v1
