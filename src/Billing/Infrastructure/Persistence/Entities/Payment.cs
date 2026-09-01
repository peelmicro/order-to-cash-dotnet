namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.payments` — remittances received
/// through the Gateway's `POST /invoices/:id/payments` (Databases doc §6).
/// Append-only: no `UpdatedAt` (§3 — ledgers whose rows are never modified
/// carry no `updated_at`). Writing one marks the invoice `paid`, releases
/// the credit hold and emits `payment.received.v1`.
/// </summary>
public sealed class Payment
{
    public Guid Id { get; set; }

    public string PaymentReference { get; set; } = string.Empty;

    public Guid InvoiceId { get; set; }

    public int Amount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime ValueDate { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
