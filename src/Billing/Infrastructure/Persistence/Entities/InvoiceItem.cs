namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.invoice_items` — lines of an invoice
/// (Databases doc §6).
/// </summary>
public sealed class InvoiceItem
{
    public Guid Id { get; set; }

    public Guid InvoiceId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public int Units { get; set; }

    public long Price { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
