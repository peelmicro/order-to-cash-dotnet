namespace OrderToCash.Gateway.Domain.Projection;

/// <summary>
/// A pure, structural mirror of the projector's <c>order_timeline</c>
/// MongoDB document (<c>src/Projector/Infrastructure/Persistence/ReadModelCollection.cs</c>,
/// <c>PlaceholderDocument.cs</c>) — the Gateway's OWN copy, not a reference
/// to the Projector assembly ("the only shared runtime code is
/// SharedKernel/Contracts/Cqrs", CLAUDE.md). Only the fields
/// <c>GET /orders</c>/<c>GET /orders/{id}</c> ever expose are modelled here
/// — the two internal-only fields the projector also stores
/// (<c>statusRank</c>, <c>processedEventKeys</c>) are simply absent, so they
/// can never leak onto the wire no matter what the Mongo query projects.
/// </summary>
public sealed record OrderReadModelParty(string? Code, string? Name, string? Gln);

public sealed record OrderReadModelTotals(long? InitialAmount, long? InitialDiscount, long? TotalAmount);

public sealed record OrderReadModelItem(string ProductCode, string? Name, int Quantity, long UnitPrice, long LineDiscount);

public sealed record OrderReadModelReferences(string? DespatchReference, string? InvoiceReference, string? PaymentReference);

/// <summary><see cref="CausationId"/> is optional because a document written before the causal-order amendment (A1) has entries with none — passed through unmapped, exactly as <see cref="EventId"/> is.</summary>
public sealed record OrderReadModelEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Summary,
    IReadOnlyDictionary<string, object?>? Detail,
    Guid? CausationId);

public sealed record OrderReadModelDocument(
    Guid OrderId,
    string? OrderReference,
    DateTimeOffset? OrderDate,
    OrderReadModelParty Retailer,
    OrderReadModelParty Company,
    string Status,
    string? CancellationReason,
    string? Currency,
    OrderReadModelTotals Totals,
    IReadOnlyList<OrderReadModelItem> Items,
    OrderReadModelReferences References,
    IReadOnlyList<OrderReadModelEvent> Events,
    bool HeaderComplete,
    DateTimeOffset UpdatedAt);
