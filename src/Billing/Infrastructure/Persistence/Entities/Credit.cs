namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.credits` — the `BuyerCredit` aggregate
/// (Databases doc §6): the credit limit granted by a supplier to a retailer,
/// one row per (retailer, company). Available credit is computed from
/// <c>credit_items</c>, not stored here. Read/written by `credit.hold` (the
/// gate before an order is confirmed) and released when the invoice is paid.
/// </summary>
public sealed class Credit
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string RetailerCode { get; set; } = string.Empty;

    public string CompanyCode { get; set; } = string.Empty;

    public long CreditLimit { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
