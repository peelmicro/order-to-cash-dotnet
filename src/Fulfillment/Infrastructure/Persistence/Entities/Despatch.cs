namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.despatches` — the `DespatchAdvice`
/// aggregate (EDI DESADV, Databases doc §5). Created by `despatch.create`
/// after the order is confirmed; emits `order.despatched.v1`. At most one
/// despatch per order (unique on `OrderReference`).
/// </summary>
public sealed class Despatch
{
    public Guid Id { get; set; }

    public string DespatchReference { get; set; } = string.Empty;

    public DateTime DespatchDate { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public string RetailerCode { get; set; } = string.Empty;

    public string OrderReference { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
