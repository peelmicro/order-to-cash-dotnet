namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.credit_items` — the hold/release ledger
/// of a credit line (Databases doc §6), one row per movement and order.
/// </summary>
public sealed class CreditItem
{
    public Guid Id { get; set; }

    public Guid CreditId { get; set; }

    public string OrderReference { get; set; } = string.Empty;

    public long Amount { get; set; }

    public string Type { get; set; } = string.Empty;

    public DateTime CreditDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
