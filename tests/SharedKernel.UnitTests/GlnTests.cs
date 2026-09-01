using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// specs/shared/requirements.md R4. specs/shared/test-matrix.md row R4.
///
/// All GLNs below carry a check digit computed independently with the
/// GS1 mod-10 algorithm stated in domain-model.md §2.4 (weights 3,1
/// alternating from the rightmost digit of the 12-digit body). See
/// progress/impl_shared_kernel.md for the verification script and its
/// cross-check against "4006381333931" — the worked EAN-13/GS1 example
/// widely published (e.g. Wikipedia's "International Article Number"
/// article), used here as an independently-sourced known-good vector.
/// </summary>
public sealed class GlnTests
{
    [Theory]
    [InlineData("4006381333931")]
    [InlineData("4890123456787")]
    [InlineData("9520012345605")]
    [InlineData("1234567890128")]
    [InlineData("0000000000000")]
    public void R4_Gln_AcceptsARealValidGlnWithACorrectCheckDigit(string validGln)
    {
        var gln = new GLN(validGln);

        Assert.Equal(validGln, gln.Value);
    }

    [Fact]
    public void R4_Gln_RefusesWrongLengthNonDigitsAndABadCheckDigit()
    {
        // 12 digits — one short of the required 13.
        var tooShort = Assert.Throws<InvalidGlnError>(() => new GLN("400638133393"));

        // 14 digits — one over the required 13.
        var tooLong = Assert.Throws<InvalidGlnError>(() => new GLN("40063813339311"));

        // A letter in place of a digit.
        var nonDigit = Assert.Throws<InvalidGlnError>(() => new GLN("400638133393A"));

        // 4006381333931 is valid; flipping its check digit from 1 to 0 must fail.
        var badCheckDigit = Assert.Throws<InvalidGlnError>(() => new GLN("4006381333930"));

        Assert.Equal("gln.invalid", tooShort.Code);
        Assert.Equal("gln.invalid", tooLong.Code);
        Assert.Equal("gln.invalid", nonDigit.Code);
        Assert.Equal("gln.invalid", badCheckDigit.Code);
    }
}
