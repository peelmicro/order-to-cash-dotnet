using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Reconstitute"/> when a persisted state
/// would already violate an invariant this aggregate exists to preserve — a
/// committed exposure already exceeding the credit limit (<b>B1</b>), an
/// entry whose currency differs from the line's (<b>B3</b>) — and by
/// <see cref="CreditLedgerEntry.Create"/> when a caller tries to construct
/// an entry whose amount is not strictly positive. A load-time or
/// construction-time fault, never a business rejection.
/// </summary>
public sealed class InvalidBuyerCreditSnapshotError(string reason)
    : DomainError("INVALID_BUYER_CREDIT_SNAPSHOT", reason)
{
}
