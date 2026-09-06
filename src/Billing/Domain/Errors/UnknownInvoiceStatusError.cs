using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised by <see cref="InvoiceStatuses.Parse"/>/<see cref="InvoiceStatuses.ToToken"/> when a status token falls outside the closed set (`issued`, `paid`) — `BI27`. Never coerced, never defaulted.</summary>
public sealed class UnknownInvoiceStatusError(string status)
    : DomainError("UNKNOWN_INVOICE_STATUS", $"'{status}' is not a recognised invoice status.")
{
    public string Status { get; } = status;
}
