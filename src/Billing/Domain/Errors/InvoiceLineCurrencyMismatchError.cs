using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised when a line's currency differs from the invoice's own currency — invariant <b>B6</b>.</summary>
public sealed class InvoiceLineCurrencyMismatchError(string invoiceCurrency, string lineCurrency)
    : DomainError("INVOICE_LINE_CURRENCY_MISMATCH", $"A line's currency '{lineCurrency}' differs from the invoice's currency '{invoiceCurrency}'.")
{
    public string InvoiceCurrency { get; } = invoiceCurrency;

    public string LineCurrency { get; } = lineCurrency;
}
