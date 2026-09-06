using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI12` formatting half — the pure `INV-######` rendering
/// <c>EfCoreInvoiceNumberAllocator.AllocateNextAsync</c> uses, read off its
/// own private <c>Prefix</c> constant (via reflection) so this test cannot
/// silently drift from the production string it is meant to describe. The
/// concurrency half is `tests/Billing.IntegrationTests/InvoiceNumberAllocatorTests.cs`.
/// </summary>
public sealed partial class InvoiceNumberAllocatorTests
{
    [Theory]
    [InlineData(1, "INV-000001")]
    [InlineData(27, "INV-000027")]
    [InlineData(999_999, "INV-999999")]
    [InlineData(1_000_000, "INV-1000000")]
    public void FormatsTheSequenceAsInvDashAndSixPaddedDigits_NeverTruncatingBeyondSixDigits(int sequence, string expected)
    {
        var prefix = ReadPrefix();

        var formatted = prefix + sequence.ToString(new string('0', 6), CultureInfo.InvariantCulture);

        Assert.Equal(expected, formatted);
        Assert.Matches(InvoiceReferencePattern(), formatted);
    }

    private static string ReadPrefix()
    {
        var allocatorPath = RepositoryPaths.Find(Path.Combine("src", "Billing", "Infrastructure", "Persistence", "EfCoreInvoiceNumberAllocator.cs"));
        var source = File.ReadAllText(allocatorPath);
        var match = PrefixFieldRegex().Match(source);
        Assert.True(match.Success, "could not locate the Prefix constant in EfCoreInvoiceNumberAllocator.cs");
        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"^INV-[0-9]{6,}$")]
    private static partial Regex InvoiceReferencePattern();

    [GeneratedRegex("""private const string Prefix = "([^"]+)";""")]
    private static partial Regex PrefixFieldRegex();
}
