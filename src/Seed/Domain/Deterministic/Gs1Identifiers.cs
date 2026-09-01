using System.Globalization;
using OrderToCash.SharedKernel;
using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.Seed.Domain.Deterministic;

/// <summary>
/// Ports #7's <c>makeGln</c> and <c>makeEan13</c>
/// (<c>apps/seed/src/deterministic.ts</c>) so this seed's GLNs and EAN
/// barcodes are byte-identical to #7's.
/// </summary>
public static class Gs1Identifiers
{
    private const long GlnPrefix = 540_000_000_000L;
    private const int GlnBodyLength = 12;
    private const string Ean13CountryPrefix = "590100";

    /// <summary>
    /// A valid 13-digit GLN built from a small integer sequence: the fixed
    /// 12-digit body <c>540000000000 + sequence</c> plus the genuine GS1
    /// mod-10 check digit. The check digit is never recomputed by hand here
    /// — per CLAUDE.md's instruction ("GLN already computes the GS1 check
    /// digit. Use it rather than reimplementing"), it is obtained by
    /// constructing <see cref="GLN"/> itself: the ten possible trailing
    /// digits are tried in order and the first one <see cref="GLN"/>'s own
    /// constructor accepts (i.e. the one whose check digit is genuinely
    /// correct) is returned. Exactly one candidate can ever validate — a
    /// mod-10 check digit is a function, not a search — so this is a single
    /// source of truth (<see cref="GLN"/>'s own validation), not a
    /// duplicated algorithm, and it fails loudly (never silently) if none
    /// of the ten candidates validates, which would only happen if
    /// <see cref="GLN"/>'s own check-digit rule ever changed shape.
    /// </summary>
    public static string MakeGln(long sequence)
    {
        if (sequence < 0 || sequence > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "makeGln: sequence out of range");
        }

        var body = (GlnPrefix + sequence).ToString(CultureInfo.InvariantCulture);
        if (body.Length != GlnBodyLength)
        {
            throw new InvalidOperationException($"makeGln: computed body is not 12 digits: {body}");
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            var candidate = body + digit.ToString(CultureInfo.InvariantCulture);
            try
            {
                return new GLN(candidate).Value;
            }
            catch (InvalidGlnError)
            {
                // Not the correct check digit — try the next candidate.
            }
        }

        // Unreachable: a mod-10 check digit always has exactly one correct
        // trailing digit in 0-9. Failing loudly here (per #7's own comment,
        // "fail loudly rather than write an invalid party identifier")
        // rather than ever returning an unvalidated GLN.
        throw new InvalidOperationException($"makeGln: no valid check digit found for body {body}");
    }

    /// <summary>
    /// A 13-digit EAN barcode with a genuine mod-10 check digit (weights
    /// 3/1 from the right) — cosmetic (no domain value object validates an
    /// EAN in this model), but a fabricated catalogue should not carry an
    /// obviously-fake barcode either.
    /// </summary>
    public static string MakeEan13(int sequence)
    {
        if (sequence < 0 || sequence > 999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "makeEan13: sequence out of range");
        }

        var body = Ean13CountryPrefix + sequence.ToString("D6", CultureInfo.InvariantCulture);

        var sum = 0;
        for (var indexFromRight = 0; indexFromRight < body.Length; indexFromRight++)
        {
            var digit = body[body.Length - 1 - indexFromRight] - '0';
            var weight = indexFromRight % 2 == 0 ? 3 : 1;
            sum += digit * weight;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return body + checkDigit.ToString(CultureInfo.InvariantCulture);
    }
}
