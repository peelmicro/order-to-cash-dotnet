namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.stock` — the `StockItem` aggregate
/// (Databases doc §5): one row per (company, product). The invariant
/// `ReservedUnits &lt;= Units` is enforced in the domain, not here; this type
/// is a plain persistence row, not the aggregate itself. Referenced only by
/// business identifiers (`CompanyCode`, `ProductCode`), never by another
/// service's internal id — database-per-service, per CLAUDE.md.
/// </summary>
public sealed class Stock
{
    public Guid Id { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public int Units { get; set; }

    public int ReservedUnits { get; set; }

    public int LowStockThreshold { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
