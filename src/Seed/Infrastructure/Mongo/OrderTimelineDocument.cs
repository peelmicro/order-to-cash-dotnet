using MongoDB.Bson.Serialization.Attributes;

namespace OrderToCash.Seed.Infrastructure.Mongo;

/// <summary>
/// The MongoDB <c>otc_read_model.order_timeline</c> document shape — a
/// structural mirror of specs/shared/openapi.yaml's <c>OrderDetail</c> plus
/// the two internal fields (<see cref="StatusRank"/>,
/// <see cref="ProcessedEventKeys"/>) the Databases doc §8 documents as
/// "projected out of every Gateway read". Every date is stored as an
/// ISO-8601 string, never a native BSON date — matching #7's own
/// <c>mongo.writer.ts</c>, whose <c>toTimelineDocument</c> calls
/// <c>.toISOString()</c> on every timestamp before writing it. IDs
/// (<c>_id</c>, <c>orderId</c>, event/causation ids) are plain lowercase,
/// hyphenated GUID strings — never the BSON UUID subtype — for the exact
/// same reason: byte-for-byte parity with #7's string ids.
/// </summary>
public sealed class OrderTimelineDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("orderReference")]
    public string? OrderReference { get; set; }

    [BsonElement("orderDate")]
    public string? OrderDate { get; set; }

    [BsonElement("retailer")]
    public PartySnapshot? Retailer { get; set; }

    [BsonElement("company")]
    public PartySnapshot? Company { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("cancellationReason")]
    public string? CancellationReason { get; set; }

    [BsonElement("currency")]
    public string? Currency { get; set; }

    [BsonElement("totals")]
    public Totals? Totals { get; set; }

    [BsonElement("items")]
    public List<TimelineItem> Items { get; set; } = [];

    [BsonElement("references")]
    public References? References { get; set; }

    /// <summary>The timeline: every fact of the order, in <c>occurredAt</c> order.</summary>
    [BsonElement("events")]
    public List<TimelineEvent> Events { get; set; } = [];

    /// <summary><see langword="false"/> while the document is a placeholder created because a fact arrived before <c>order.placed.v1</c>; always <see langword="true"/> for a seeded document.</summary>
    [BsonElement("headerComplete")]
    public bool HeaderComplete { get; set; }

    [BsonElement("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>Internal (Databases doc §8): monotonic rank of <see cref="Status"/> so an older fact can never move the status backwards. Not part of the client-visible <c>OrderDetail</c> shape.</summary>
    [BsonElement("statusRank")]
    public int StatusRank { get; set; }

    /// <summary>A local copy of the projector's own <c>TIMELINE_ORDER_VERSION</c> — stamped at the current version so a seeded document is never mistaken for one that needs the projector's boot migration.</summary>
    [BsonElement("timelineOrderVersion")]
    public int TimelineOrderVersion { get; set; }

    /// <summary>Internal (Databases doc §8): the event ids already applied to this document — the projector's own idempotency dedup key shape, <c>projector:&lt;eventId&gt;</c>.</summary>
    [BsonElement("processedEventKeys")]
    public List<string> ProcessedEventKeys { get; set; } = [];
}

public sealed class PartySnapshot
{
    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("gln")]
    public string Gln { get; set; } = string.Empty;
}

public sealed class Totals
{
    [BsonElement("initialAmount")]
    public long InitialAmount { get; set; }

    [BsonElement("initialDiscount")]
    public long InitialDiscount { get; set; }

    [BsonElement("totalAmount")]
    public long TotalAmount { get; set; }
}

public sealed class TimelineItem
{
    [BsonElement("productCode")]
    public string ProductCode { get; set; } = string.Empty;

    [BsonElement("name")]
    public string? Name { get; set; }

    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("unitPrice")]
    public long UnitPrice { get; set; }

    [BsonElement("lineDiscount")]
    public long LineDiscount { get; set; }
}

public sealed class References
{
    [BsonElement("despatchReference")]
    public string? DespatchReference { get; set; }

    [BsonElement("invoiceReference")]
    public string? InvoiceReference { get; set; }

    [BsonElement("paymentReference")]
    public string? PaymentReference { get; set; }
}

public sealed class TimelineEvent
{
    [BsonElement("eventId")]
    public string EventId { get; set; } = string.Empty;

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("occurredAt")]
    public string OccurredAt { get; set; } = string.Empty;

    [BsonElement("summary")]
    public string Summary { get; set; } = string.Empty;

    [BsonElement("detail")]
    [BsonIgnoreIfNull]
    public Dictionary<string, object>? Detail { get; set; }

    /// <summary>The fixture's own declared causal edge — never inferred or reconstructed from array position.</summary>
    [BsonElement("causationId")]
    public string CausationId { get; set; } = string.Empty;
}
