using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised by <see cref="Invoice.MarkPaid"/> when the payment's amount does not equal the invoice's <c>totalAmount</c> exactly — invariant <b>B10</b>.</summary>
public sealed class InvoicePaymentAmountMismatchError(long expectedMinorUnits, long receivedMinorUnits)
    : DomainError("INVOICE_PAYMENT_AMOUNT_MISMATCH", $"Payment amount {receivedMinorUnits} does not equal the invoice's totalAmount {expectedMinorUnits}.")
{
    public long ExpectedMinorUnits { get; } = expectedMinorUnits;

    public long ReceivedMinorUnits { get; } = receivedMinorUnits;
}
