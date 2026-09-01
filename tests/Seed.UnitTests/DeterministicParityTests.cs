using System.Security.Cryptography;
using System.Text;
using OrderToCash.Seed.Domain.Deterministic;
using OrderToCash.SharedKernel;
using Xunit;
using Data = OrderToCash.Seed.Domain.Data;
using Sagas = OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.UnitTests;

/// <summary>
/// Feature seed_job — "the derivation helpers match #7 exactly": every
/// expected value below was obtained by running #7's OWN TypeScript
/// (<c>apps/seed/src/deterministic.ts</c>), not by running this C# port and
/// writing down what it said — a value seeded from this port's own output
/// would prove only that the port is self-consistent, not that it matches
/// #7.
///
/// <b>How the expected values were obtained</b> (recorded verbatim in
/// progress/impl_seed_job.md): from the #7 checkout
/// (<c>order-to-cash-nestjs</c>), <c>node -e</c> scripts that inline #7's
/// own <c>deterministicId</c>/<c>makeEan13</c>/<c>GLN.computeCheckDigit</c>
/// algorithms (copy-pasted verbatim from <c>apps/seed/src/deterministic.ts</c>
/// and <c>packages/shared-kernel/src/domain/gln.ts</c>) and print the
/// results for the exact namespaces/sequences this seed also uses
/// (<c>currency:USD</c>, <c>retailer:CarrefourEs</c>, <c>order:1</c>,
/// <c>product:PRD-0001</c>, <c>stock:IBERFOODS:PRD-0002</c>, EAN sequences
/// 1 and 12, GLN sequences 1-7 and 21).
/// </summary>
public sealed class DeterministicParityTests
{
    [Theory]
    [InlineData("currency:USD", "8a2ac568-0944-4507-872a-38acbce9724c")]
    [InlineData("currency:EUR", "23ab1a2b-bce4-4b83-8304-6b5a1084990c")]
    [InlineData("currency:GBP", "ac45711f-e2ac-456b-975f-8aef85070564")]
    [InlineData("retailer:CarrefourEs", "0e47f181-c92e-416a-bff1-5c8d497768b1")]
    [InlineData("order:1", "1741d5aa-cfba-4205-a1c0-82e7a5cb8984")]
    [InlineData("product:PRD-0001", "1164d610-b1d9-493c-980e-1b4a37d00e1e")]
    [InlineData("stock:IBERFOODS:PRD-0002", "9ad0863b-e61e-4e1d-9488-c60850b82779")]
    public void DeterministicId_Matches_The_Value_Number7s_TypeScript_Produced(string @namespace, string expected)
    {
        var actual = DeterministicId.Of(@namespace);

        Assert.Equal(Guid.Parse(expected), actual);
    }

    [Fact]
    public void DeterministicId_Is_Stable_Across_Calls_With_The_Same_Namespace()
    {
        var first = DeterministicId.Of("order:42");
        var second = DeterministicId.Of("order:42");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Ties the ACTUAL production dataset (<c>Currencies.All</c>,
    /// <c>Retailers.All</c>, <c>SagaFixtures.All</c>) to the same oracle
    /// values above — not just <c>DeterministicId.Of</c> called directly
    /// with a hand-typed namespace string. Without this, a broken namespace
    /// literal INSIDE <c>CurrencySeed.cs</c>/<c>RetailerSeed.cs</c>/
    /// <c>SagaFixtures.cs</c> (as opposed to inside this test file) would
    /// slip past every other parity test here undetected — arming table
    /// entry "break one namespace string (the id test must fail)".
    /// </summary>
    [Fact]
    public void The_Seeded_Datasets_Own_Ids_Match_The_Value_Number7s_TypeScript_Produced()
    {
        var usd = Data.Currencies.All.Single(c => c.Code == "USD");
        Assert.Equal(Guid.Parse("8a2ac568-0944-4507-872a-38acbce9724c"), usd.Id);

        var carrefourEs = Data.Retailers.All.Single(r => r.Code == "CarrefourEs");
        Assert.Equal(Guid.Parse("0e47f181-c92e-416a-bff1-5c8d497768b1"), carrefourEs.Id);

        var order1 = Sagas.SagaFixtures.All.Single(s => s.Sequence == 1);
        Assert.Equal(Guid.Parse("1741d5aa-cfba-4205-a1c0-82e7a5cb8984"), order1.OrderId);
    }

    [Theory]
    [InlineData(1, "5901000000012")]
    [InlineData(12, "5901000000128")]
    public void MakeEan13_Matches_The_Value_Number7s_TypeScript_Produced(int sequence, string expected)
    {
        Assert.Equal(expected, Gs1Identifiers.MakeEan13(sequence));
    }

    [Theory]
    [InlineData(1, "5400000000010")]
    [InlineData(2, "5400000000027")]
    [InlineData(3, "5400000000034")]
    [InlineData(4, "5400000000041")]
    [InlineData(5, "5400000000058")]
    [InlineData(6, "5400000000065")]
    [InlineData(7, "5400000000072")]
    [InlineData(21, "5400000000218")]
    public void MakeGln_Matches_The_Value_Number7s_TypeScript_Produced(int sequence, string expected)
    {
        Assert.Equal(expected, Gs1Identifiers.MakeGln(sequence));
    }

    /// <summary>
    /// review_seed_job.md D4: the original version of this test asserted
    /// <c>NotEqual</c> against the correct value with its LAST character
    /// hand-edited, then <c>Equal</c> against the correct value — the
    /// second line made the first redundant with
    /// <see cref="DeterministicId_Matches_The_Value_Number7s_TypeScript_Produced"/>
    /// above, and the first proved nothing about the wart, because the
    /// wart changes the THIRD hyphen group (<c>timeHiAndVersion</c>), not
    /// the last. This version independently reconstructs the "fixed"
    /// (un-warted) derivation locally — <c>hex[12..15]</c> instead of
    /// production's actual <c>hex[13..16]</c> — and proves
    /// <see cref="DeterministicId.Of"/> does NOT produce that value: the
    /// wart is genuinely load-bearing in the shipped code, not merely
    /// documented as such. The "fixed" value below
    /// (<c>8a2ac568-0944-4250-...</c>, third group <c>4250</c> instead of
    /// production's <c>4507</c>) is #7's own oracle for what "fixing" the
    /// wart would produce, from the reviewer's own re-derivation.
    /// </summary>
    [Fact]
    public void DeterministicId_Would_Differ_If_The_Skipped_Hex_Character_Were_Not_Skipped()
    {
        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("otc-seed:currency:USD")));
        var timeLow = hex[..8];
        var timeMid = hex[8..12];
        var unWartedTimeHiAndVersion = "4" + hex[12..15]; // the "corrected" slice — never used in production
        var variantNibbleValue = (Convert.ToInt32(hex[16].ToString(), 16) & 0x3) | 0x8;
        var variantNibble = variantNibbleValue.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        var clockSeqAndReserved = variantNibble + hex[17..20];
        var node = hex[20..32];
        var unWarted = Guid.Parse($"{timeLow}-{timeMid}-{unWartedTimeHiAndVersion}-{clockSeqAndReserved}-{node}");

        var actual = DeterministicId.Of("currency:USD");

        Assert.Equal(Guid.Parse("8a2ac568-0944-4250-872a-38acbce9724c"), unWarted);
        Assert.NotEqual(unWarted, actual);
        Assert.Equal(Guid.Parse("8a2ac568-0944-4507-872a-38acbce9724c"), actual);
    }

    [Fact]
    public void MakeGln_Always_Produces_A_Value_That_Validates_Against_Gln()
    {
        for (var sequence = 0; sequence <= 30; sequence++)
        {
            var gln = Gs1Identifiers.MakeGln(sequence);
            var validated = new GLN(gln);

            Assert.Equal(gln, validated.Value);
        }
    }
}
