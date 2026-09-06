using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised when <c>amount − discount</c> would be negative — invariant <b>B6</b>. A discount may never exceed the amount it discounts.</summary>
public sealed class NegativeInvoiceTotalError(long amountMinorUnits, long discountMinorUnits)
    : DomainError("NEGATIVE_INVOICE_TOTAL", $"totalAmount would be negative: amount {amountMinorUnits} minus discount {discountMinorUnits}.")
{
    public long AmountMinorUnits { get; } = amountMinorUnits;

    public long DiscountMinorUnits { get; } = discountMinorUnits;
}
