using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="Invoice.MarkPaid"/> when the payment's amount does
/// not equal the invoice's <c>totalAmount</c> exactly — invariant <b>B10</b>.
/// </summary>
/// <remarks>
/// Backlog id 102: this message reaches a human — traced
/// <c>BillingErrorMapper.cs</c> -&gt; Gateway <c>ProblemJsonMiddleware.cs</c>'s
/// <c>detail</c> -&gt; the web app's problem-document display. Rendered with
/// the shared money-text formatter (id 100), never as a raw minor-units
/// integer. <paramref name="currency"/> is the invoice's own currency (both
/// amounts share it — <see cref="Invoice.MarkPaid"/> checks that BEFORE
/// this error can be raised).
/// </remarks>
public sealed class InvoicePaymentAmountMismatchError(long expectedMinorUnits, long receivedMinorUnits, string currency)
    : DomainError(
        "INVOICE_PAYMENT_AMOUNT_MISMATCH",
        $"Payment amount {MoneyText.Format(receivedMinorUnits, currency)} does not equal the invoice's totalAmount {MoneyText.Format(expectedMinorUnits, currency)}.")
{
    public long ExpectedMinorUnits { get; } = expectedMinorUnits;

    public long ReceivedMinorUnits { get; } = receivedMinorUnits;
}
