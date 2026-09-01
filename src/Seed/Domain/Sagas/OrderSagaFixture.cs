namespace OrderToCash.Seed.Domain.Sagas;

/// <summary>One order line of a fabricated saga — ported from #7's <c>OrderLineFixture</c>.</summary>
public sealed record OrderLineFixture(string ProductCode, string Description, int Quantity, long UnitPrice, long LineDiscount);

/// <summary>
/// One already-published outbox row a fabricated saga writes — ported from
/// #7's <c>OutboxFixture</c>. <see cref="Payload"/> is <see cref="object"/>
/// (never a domain type) because it holds one of the sealed
/// <c>OrderToCash.Contracts.Facts.Payloads.*</c> records, which have no
/// common base type by design.
/// </summary>
public sealed record OutboxFixture(
    Guid Id,
    Guid EventId,
    string EventType,
    Guid AggregateId,
    Guid CorrelationId,
    // The eventId of the causing fact, or the id of a synthetic command —
    // see SagaFixtures's header comment for the causal-chain rule.
    Guid CausationId,
    object Payload,
    DateTime OccurredAt,
    DateTime PublishedAt);

/// <summary>One `order_timeline` event entry — ported from #7's <c>TimelineEntryFixture</c>.</summary>
public sealed record TimelineEntryFixture(
    Guid EventId,
    string EventType,
    DateTime OccurredAt,
    string Summary,
    Guid CausationId,
    IReadOnlyDictionary<string, object>? Detail = null);

/// <summary>One stock reservation a fabricated saga created — ported from #7's <c>ReservationFixture</c>.</summary>
public sealed record ReservationFixture(
    Guid Id,
    string CompanyCode,
    string RetailerCode,
    string ProductCode,
    int Units,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>One despatched line — ported from #7's inline <c>DespatchFixture.items</c> entry shape.</summary>
public sealed record DespatchItemFixture(string ProductCode, int Units);

/// <summary>The despatch a completed saga created — ported from #7's <c>DespatchFixture</c>.</summary>
public sealed record DespatchFixture(
    Guid Id,
    string DespatchReference,
    DateTime DespatchDate,
    string CompanyCode,
    string RetailerCode,
    IReadOnlyList<DespatchItemFixture> Items);

/// <summary>One credit ledger movement — ported from #7's <c>CreditLedgerEntryFixture</c>.</summary>
public sealed record CreditLedgerEntryFixture(
    Guid Id,
    Guid CreditId,
    string OrderReference,
    long Amount,
    string Type,
    DateTime CreditDate);

/// <summary>One invoiced line — ported from #7's inline <c>InvoiceFixture.items</c> entry shape.</summary>
public sealed record InvoiceItemFixture(string ProductCode, int Units, long Price);

/// <summary>The payment that settled an invoice — ported from #7's inline <c>InvoiceFixture.payment</c> shape.</summary>
public sealed record PaymentFixture(Guid Id, string PaymentReference, long Amount, DateTime ValueDate, string Source);

/// <summary>The invoice a completed saga issued — ported from #7's <c>InvoiceFixture</c>.</summary>
public sealed record InvoiceFixture(
    Guid Id,
    string InvoiceReference,
    DateTime InvoiceDate,
    long Amount,
    long Discount,
    long TotalAmount,
    string Status,
    DateTime PaidAt,
    IReadOnlyList<InvoiceItemFixture> Items,
    PaymentFixture Payment);

/// <summary>
/// The fabricated saga history — one completed or cancelled order and every
/// fact/row it produced, ported from #7's <c>OrderSagaFixture</c>
/// (<c>apps/seed/src/data/sagas.data.ts</c>). Built ONCE per order and then
/// fanned out by the Infrastructure writers into the three MS-SQL databases
/// (already-published outbox rows) and the MongoDB <c>order_timeline</c>
/// document, so the four stores can never disagree about what happened.
/// </summary>
public sealed record OrderSagaFixture(
    int Sequence,
    Guid OrderId,
    string OrderReference,
    DateTime OrderDate,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    string Status,
    string? CancellationReason,
    IReadOnlyList<OrderLineFixture> Lines,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    DateTime UpdatedAt,
    IReadOnlyList<OutboxFixture> OrdersOutbox,
    IReadOnlyList<ReservationFixture> Reservations,
    DespatchFixture? Despatch,
    IReadOnlyList<OutboxFixture> FulfillmentOutbox,
    IReadOnlyList<CreditLedgerEntryFixture> CreditLedgerEntries,
    InvoiceFixture? Invoice,
    IReadOnlyList<OutboxFixture> BillingOutbox,
    IReadOnlyList<TimelineEntryFixture> Timeline);
