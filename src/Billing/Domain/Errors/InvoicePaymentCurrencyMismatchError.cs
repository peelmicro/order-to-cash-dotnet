using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised by <see cref="Invoice.MarkPaid"/> when the payment's currency differs from the invoice's own — invariant <b>B10</b>.</summary>
public sealed class InvoicePaymentCurrencyMismatchError(string expected, string received)
    : DomainError("INVOICE_PAYMENT_CURRENCY_MISMATCH", $"Payment currency '{received}' does not match the invoice's currency '{expected}'.")
{
    public string Expected { get; } = expected;

    public string Received { get; } = received;
}
