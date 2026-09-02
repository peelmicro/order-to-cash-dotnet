namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.invoices` — the `Invoice` aggregate (EDI
/// INVOIC, Databases doc §6). Created as `issued` by `invoice.issue` after
/// the despatch; moves to `paid` when a remittance arrives. One invoice per
/// order (unique on `OrderReference`).
/// </summary>
public sealed class Invoice
{
    public Guid Id { get; set; }

    public string InvoiceReference { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public string RetailerCode { get; set; } = string.Empty;

    public string OrderReference { get; set; } = string.Empty;

    public long Amount { get; set; }

    public long Discount { get; set; }

    public long TotalAmount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
