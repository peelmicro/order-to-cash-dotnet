namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.reservations` — one row per order
/// line reserved (Databases doc §5). Created `reserved` by `stock.reserve`;
/// flipped to `released` (compensation) or `consumed` (`despatch.create`).
/// </summary>
public sealed class Reservation
{
    public Guid Id { get; set; }

    public Guid StockId { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public string RetailerCode { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string OrderReference { get; set; } = string.Empty;

    public int Units { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
