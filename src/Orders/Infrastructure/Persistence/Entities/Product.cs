namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.products` (Databases doc §4.1). `Price`
/// is the *current* catalogue price; each order line snapshots its own
/// price at order time, so a later catalogue change never rewrites an
/// existing order.
/// </summary>
public sealed class Product
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Ean { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int Price { get; set; }

    public Guid CurrencyId { get; set; }

    public DateTime? DisabledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
