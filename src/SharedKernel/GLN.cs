using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// A Global Location Number — the EDI party identifier for a retailer
/// (buyer) or a supplier: exactly 13 decimal digits whose final digit is a
/// GS1 mod-10 check digit over the preceding twelve
/// (specs/shared/requirements.md R4; domain-model.md §2.4).
/// </summary>
public readonly record struct GLN
{
    private const int Length = 13;
    private const int BodyLength = 12;

    public GLN(string value)
    {
        if (!IsThirteenDigits(value))
        {
            throw new InvalidGlnError(value ?? "<null>");
        }

        var expectedCheckDigit = ComputeCheckDigit(value.AsSpan(0, BodyLength));
        var actualCheckDigit = value[BodyLength] - '0';

        if (actualCheckDigit != expectedCheckDigit)
        {
            throw new InvalidGlnError(value);
        }

        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// GS1 mod-10 check digit over a 12-digit body: multiply digits 1..12
    /// alternately by 3 and 1 starting from the right of the body (position
    /// 12 × 3, position 11 × 1, …), sum the products, and the check digit is
    /// <c>(10 - (sum mod 10)) mod 10</c> (domain-model.md §2.4).
    /// </summary>
    private static int ComputeCheckDigit(ReadOnlySpan<char> body)
    {
        var sum = 0;

        for (var indexFromRight = 0; indexFromRight < body.Length; indexFromRight++)
        {
            var digit = body[body.Length - 1 - indexFromRight] - '0';
            var weight = indexFromRight % 2 == 0 ? 3 : 1;
            sum += digit * weight;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static bool IsThirteenDigits(string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value;
}
