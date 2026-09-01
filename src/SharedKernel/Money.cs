using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// Every monetary amount in this system: an integer count of minor units
/// together with an ISO 4217 alpha-3 currency code (specs/shared/requirements.md
/// R1; domain-model.md §2.1, invariant M1). There is deliberately no decimal,
/// floating-point or fixed-point major-unit representation anywhere on this
/// type — no <c>decimal</c> property, no implicit or explicit conversion, no
/// formatting method. Presenting an amount for humans is a presentation
/// concern that belongs outside the domain.
/// </summary>
/// <remarks>
/// Equality (<c>==</c>/<c>Equals</c>) is the record struct's ordinary
/// value equality over (<see cref="MinorUnits"/>, <see cref="Currency"/>):
/// two amounts in different currencies are simply unequal, not an error.
/// <see cref="CompareTo"/> and the relational operators are a different
/// operation — deciding *which amount is larger* — and that is exactly the
/// "comparison" invariant M2 refuses to perform across currencies, because
/// there is no rate to make one order's total meaningfully larger than
/// another currency's without a conversion this model does not have.
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    public Money(long minorUnits, string currency)
    {
        if (!IsWellFormedCurrencyCode(currency))
        {
            throw new InvalidCurrencyCodeError(currency);
        }

        MinorUnits = minorUnits;
        Currency = currency;
    }

    /// <summary>Integer count of the currency's smallest denomination — never a decimal major-unit amount (M1).</summary>
    public long MinorUnits { get; }

    /// <summary>ISO 4217 alpha-3 currency code, e.g. "EUR", "GBP", "USD".</summary>
    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);

    /// <summary>M3 — closed arithmetic: returns a <see cref="Money"/> of the same currency, or raises M2.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(MinorUnits + other.MinorUnits, Currency);
    }

    /// <summary>M3 — closed arithmetic: returns a <see cref="Money"/> of the same currency, or raises M2.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(MinorUnits - other.MinorUnits, Currency);
    }

    /// <summary>M3 — multiply by a <see cref="Quantity"/>, returning a <see cref="Money"/> of the same currency. Division is deliberately not offered.</summary>
    public Money Multiply(Quantity quantity) => new(MinorUnits * quantity.Value, Currency);

    /// <summary>M4 — a negative amount is representable (discounts, reversals); rejecting a negative *total* is the caller's invariant, not this type's.</summary>
    public bool IsNegative => MinorUnits < 0;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money left, Quantity right) => left.Multiply(right);

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public override string ToString() => $"{MinorUnits} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchError(Currency, other.Currency);
        }
    }

    private static bool IsWellFormedCurrencyCode(string? currency)
    {
        if (currency is null || currency.Length != 3)
        {
            return false;
        }

        foreach (var character in currency)
        {
            if (character is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
