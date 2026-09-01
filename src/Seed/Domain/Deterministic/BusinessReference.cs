using System.Globalization;

namespace OrderToCash.Seed.Domain.Deterministic;

/// <summary>
/// Formats the three business references <see cref="SharedKernel.OrderNumber"/>
/// does not cover: <c>DES-######</c>, <c>INV-######</c>, <c>CR-######</c>
/// (domain-model.md §2.3, mirroring #7's shared-kernel
/// <c>DespatchReference</c> / <c>InvoiceReference</c> / <c>CreditLineReference</c>
/// value objects — <c>&lt;PREFIX&gt;-</c> followed by a zero-padded,
/// six-digit-minimum sequence). No such value objects exist yet in this
/// repository's <c>SharedKernel</c> (only <c>OrderNumber</c> does), and this
/// feature's task instructions forbid touching <c>SharedKernel</c>, so the
/// same zero-pad-six format <see cref="SharedKernel.OrderNumber"/> itself
/// uses is reproduced here, locally, for the three references this seed
/// also has to write.
/// </summary>
public static class BusinessReference
{
    private const int MinimumSequenceDigits = 6;

    public static string Despatch(long sequence) => Format("DES-", sequence);

    public static string Invoice(long sequence) => Format("INV-", sequence);

    public static string Credit(long sequence) => Format("CR-", sequence);

    private static string Format(string prefix, long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, $"{prefix}: sequence must be positive");
        }

        return prefix + sequence.ToString(new string('0', MinimumSequenceDigits), CultureInfo.InvariantCulture);
    }
}
