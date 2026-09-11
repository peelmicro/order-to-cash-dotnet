namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.orders` — the write model of the `Order`
/// aggregate (Databases doc §4.2). `Status` *is* the order state machine and
/// doubles as the saga state. This type is a plain persistence row, not the
/// domain aggregate itself: the Orders domain model lands in feature
/// <c>orders_aggregate</c> (phase 8), which will map to/from this row
/// through a repository. Keeping the two separate now means this schema
/// feature carries no domain behaviour, per CLAUDE.md's layering rule.
/// </summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public string OrderReference { get; set; } = string.Empty;

    /// <summary>
    /// Feature <c>observability_reliability</c>, <c>RI1</c>/<c>RI2</c>/<c>RI3</c>/<c>RI4</c> —
    /// the client-supplied `orders.create` idempotency key, nullable because
    /// almost no caller supplies one. <c>OrderConfiguration</c>'s filtered
    /// unique index is what makes a nullable column dedupe correctly on
    /// MS-SQL (design.md §2.1, ledger L1): unfiltered, MS-SQL treats two
    /// `NULL`s as equal and would admit only one row with none.
    /// </summary>
    public Guid? RequestId { get; set; }          // request_id uniqueidentifier NULL

    public DateTime OrderDate { get; set; }

    public Guid CompanyId { get; set; }

    public Guid RetailerId { get; set; }

    public Guid CurrencyId { get; set; }

    public long InitialAmount { get; set; }

    public long InitialDiscount { get; set; }

    public long TotalAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? CancellationReason { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}
