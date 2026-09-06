using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="CreditExposure.Summarise"/> when a summation over a
/// credit line's ledger — per-order exposure, committed exposure, open
/// exposure — would exceed the range of a 64-bit signed integer (`BC30`,
/// design.md §3.3, §15 <c>L25</c>). Wraps the <see cref="OverflowException"/>
/// the <c>checked</c> region raised, rather than a wrapped value. Mapped
/// TERMINAL, deliberately: a ledger whose sums overflow will overflow again
/// on every retry, so a retryable code would spin the saga forever against
/// a state only an operator can repair.
/// </summary>
public sealed class CreditLedgerOverflowError(OverflowException inner)
    : DomainError("CREDIT_LEDGER_OVERFLOW", "A summation over the credit ledger overflowed a 64-bit signed integer.")
{
    public OverflowException Inner { get; } = inner;
}
