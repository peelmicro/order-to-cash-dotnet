using OrderToCash.Billing.Domain.Errors;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// The three movements an append-only credit ledger can record —
/// domain-model.md §5.1's <c>credit_items.type</c> column. The enum is the
/// domain vocabulary; <see cref="CreditEntryTypes"/> carries the lowercase
/// wire/storage tokens, the same split <c>ReservationStatus</c>/
/// <c>ReservationStatuses</c> already establishes.
/// </summary>
public enum CreditEntryType
{
    Hold,
    Consume,
    Release,
}

/// <summary>
/// Maps <see cref="CreditEntryType"/> to and from the lowercase tokens the
/// <c>type</c> column stores — an explicit table, never a case transform of
/// the C# member name. <see cref="Parse"/> refuses any token outside the
/// closed set rather than silently ignoring the row (design.md §3.2, §15
/// <c>L12</c>): a skipped row would move <c>Σ</c> and therefore
/// <c>availableCredit</c>.
/// </summary>
public static class CreditEntryTypes
{
    public static string ToToken(CreditEntryType type) => type switch
    {
        CreditEntryType.Hold => "hold",
        CreditEntryType.Consume => "consume",
        CreditEntryType.Release => "release",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unrecognised CreditEntryType member."),
    };

    public static CreditEntryType Parse(string? token) => token switch
    {
        "hold" => CreditEntryType.Hold,
        "consume" => CreditEntryType.Consume,
        "release" => CreditEntryType.Release,
        _ => throw new UnknownCreditEntryTypeError(token),
    };
}
