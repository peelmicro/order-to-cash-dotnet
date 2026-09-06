using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="CreditEntryTypes.Parse"/> when a stored or
/// wire-received token is outside the closed three-value set — a load-time
/// fault (a typo in the persistence column, which is free-text
/// <c>nvarchar(20)</c>), not a business rejection. A silently-skipped row
/// would move <c>Σ</c> and therefore <c>availableCredit</c> — the failure
/// would present as a wrong credit limit, not as a parse error (design.md
/// §3.2, §15 <c>L12</c>).
/// </summary>
public sealed class UnknownCreditEntryTypeError(string? token)
    : DomainError("UNKNOWN_CREDIT_ENTRY_TYPE", $"'{token ?? "<null>"}' is not a recognised CreditEntryType token.")
{
    public string? Token { get; } = token;
}
