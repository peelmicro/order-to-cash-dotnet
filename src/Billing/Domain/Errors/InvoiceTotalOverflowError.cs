using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="Invoice.Issue"/> when <c>Σ(unitPrice × units)</c> or
/// <c>amount − discount</c> would exceed the representable range of a 64-bit
/// signed integer (`BI25`, design.md §3.4, §16 ledger <c>L7</c>). Wraps the
/// <see cref="OverflowException"/> the `checked` region raised, exactly the
/// shape <see cref="CreditLedgerOverflowError"/> already ships. Mapped
/// TERMINAL, deliberately: a permanently-overflowing payload would be
/// retried on every sweep forever if it fell to the transient
/// <c>INTERNAL_ERROR</c> instead.
/// </summary>
public sealed class InvoiceTotalOverflowError(OverflowException inner)
    : DomainError("INVOICE_TOTAL_OVERFLOW", "A summation over the invoice's lines overflowed a 64-bit signed integer.")
{
    public OverflowException Inner { get; } = inner;
}
