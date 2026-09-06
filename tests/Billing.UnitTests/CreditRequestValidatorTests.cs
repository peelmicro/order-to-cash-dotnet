using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>One case per validation rule of design.md §4.4 — the ledger `L3`/`L13`/`L14` alphabets and the paging defaults (`L10`).</summary>
public sealed class CreditRequestValidatorTests
{
    private static CreditHoldRequestPayload ValidHold() =>
        new("ORD-000001", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR"));

    [Fact]
    public void ValidateHold_AcceptsAWellFormedRequest()
    {
        CreditRequestValidator.ValidateHold(ValidHold());
    }

    [Theory]
    [InlineData("ord-000001")]
    [InlineData("ORD-1")]
    [InlineData("ORD000001")]
    [InlineData(null)]
    public void ValidateHold_RejectsAMalformedOrderReference(string? orderReference)
    {
        var request = ValidHold() with { OrderReference = orderReference! };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateHold_RejectsAnEmptyOrMissingRetailerCode(string? retailerCode)
    {
        var request = ValidHold() with { RetailerCode = retailerCode! };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateHold_RejectsARetailerCodeLongerThanTwentyCharacters()
    {
        var request = ValidHold() with { RetailerCode = new string('A', 21) };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateHold_RejectsANonAsciiRetailerCode()
    {
        var request = ValidHold() with { RetailerCode = "Carréfour" };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateHold_RejectsAnEmptyOrMissingCompanyCode(string? companyCode)
    {
        var request = ValidHold() with { CompanyCode = companyCode! };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateHold_RejectsAMissingAmount()
    {
        var request = ValidHold() with { Amount = null! };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateHold_RejectsANegativeAmount()
    {
        var request = ValidHold() with { Amount = new CreditMoney(-1, "EUR") };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateHold_AcceptsAZeroAmount()
    {
        CreditRequestValidator.ValidateHold(ValidHold() with { Amount = new CreditMoney(0, "EUR") });
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData(null)]
    public void ValidateHold_RejectsAMalformedCurrencyCode(string? currency)
    {
        var request = ValidHold() with { Amount = new CreditMoney(1_000, currency!) };
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateHold(request));
    }

    [Fact]
    public void ValidateRelease_AcceptsAWellFormedRequest()
    {
        CreditRequestValidator.ValidateRelease(new CreditReleaseRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS"));
    }

    [Fact]
    public void ValidateRelease_RejectsAMalformedOrderReference()
    {
        var request = new CreditReleaseRequestPayload("bad", "CarrefourEs", "IBERFOODS");
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateRelease(request));
    }

    [Fact]
    public void ValidateList_AcceptsNoPagingFields_TheDefaultsApplyElsewhere()
    {
        CreditRequestValidator.ValidateList(new CreditListRequestPayload(null, null));
    }

    [Fact]
    public void ValidateList_RejectsAPageBelowOne()
    {
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateList(new CreditListRequestPayload(0, null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ValidateList_RejectsAPageSizeOutsideOneToTwoHundred(int pageSize)
    {
        Assert.Throws<InvalidCreditRequestError>(() => CreditRequestValidator.ValidateList(new CreditListRequestPayload(1, pageSize)));
    }

    [Fact]
    public void ValidateList_AcceptsThePagingDefaults_PageOnePageSizeTwentyFive()
    {
        // L10: the validator itself does not SUPPLY the defaults (the
        // service layer does) — it only refuses an EXPLICIT out-of-range
        // value. Page 1 / pageSize 25 are always valid.
        CreditRequestValidator.ValidateList(new CreditListRequestPayload(1, 25));
    }
}
