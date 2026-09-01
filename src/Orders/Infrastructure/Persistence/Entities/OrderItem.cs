namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.order_items` — the order lines
/// (Databases doc §4.2). `Description` and `Price` are snapshots taken at
/// order time, not joins into <see cref="Product"/>.
/// </summary>
public sealed class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public string Description { get; set; } = string.Empty;

    public int Price { get; set; }

    public int Quantity { get; set; }

    public int Discount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
