namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.currencies` — one of the seeded reference
/// catalogues (Databases doc §4.1). Deliberately a plain persistence type,
/// not the domain aggregate: the Orders domain model (feature
/// <c>orders_aggregate</c>, phase 8) has not landed yet, and this feature
/// (<c>db_orders</c>, phase 6) is schema-only.
/// </summary>
public sealed class Currency
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string IsoNumber { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public int DecimalPoints { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
