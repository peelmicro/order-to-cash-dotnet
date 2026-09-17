using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised when <c>amount − discount</c> would be negative — invariant <b>B6</b>. A discount may never exceed the amount it discounts.</summary>
/// <remarks>
/// Backlog id 102: reaches a human via <c>BillingErrorMapper</c> -&gt;
/// Gateway problem+json <c>detail</c>. Rendered with the shared money-text
/// formatter (id 100). <paramref name="currency"/> is the invoice's own
/// currency, both amounts share it by construction (<see cref="Invoice.Issue"/>).
/// </remarks>
public sealed class NegativeInvoiceTotalError(long amountMinorUnits, long discountMinorUnits, string currency)
    : DomainError(
        "NEGATIVE_INVOICE_TOTAL",
        $"totalAmount would be negative: amount {MoneyText.Format(amountMinorUnits, currency)} minus discount {MoneyText.Format(discountMinorUnits, currency)}.")
{
    public long AmountMinorUnits { get; } = amountMinorUnits;

    public long DiscountMinorUnits { get; } = discountMinorUnits;
}
