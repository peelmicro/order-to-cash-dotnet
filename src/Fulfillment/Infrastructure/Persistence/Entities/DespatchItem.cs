namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.despatch_items` — lines of a
/// despatch (Databases doc §5).
/// </summary>
public sealed class DespatchItem
{
    public Guid Id { get; set; }

    public Guid DespatchId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public int Units { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
