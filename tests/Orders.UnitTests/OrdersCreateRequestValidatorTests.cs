using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// review A2 — <c>asyncapi.yaml</c>'s <c>OrdersCreateRequestPayload</c>
/// required fields, checked BEFORE <c>ToCommand</c> ever runs, so a
/// malformed request is a clean <c>VALIDATION_FAILED</c> rather than a
/// <see cref="NullReferenceException"/> the responder's catch-all turns into
/// <c>INTERNAL_ERROR</c>.
/// </summary>
public sealed class OrdersCreateRequestValidatorTests
{
    private static OrdersCreateRequestPayload ValidRequest() => new(
        RequestId: null,
        RetailerCode: "RETAILER-01",
        CompanyCode: "COMPANY-01",
        Currency: "EUR",
        Lines: [new OrdersCreateRequestLine("PROD-001", 1, null, null)],
        OrderDiscount: null,
        Notes: null);

    [Fact]
    public void Validate_AWellFormedRequest_ThrowsNothing()
    {
        var exception = Record.Exception(() => OrdersCreateRequestValidator.Validate(ValidRequest()));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ARequestWithNoLinesAtAll_RefusesRatherThanLettingToCommandNullReference()
    {
        var request = ValidRequest() with { Lines = null! };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("lines", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ARequestWithAnEmptyLinesArray_Refuses()
    {
        var request = ValidRequest() with { Lines = [] };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("lines", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_AMissingOrBlankRetailerCode_Refuses(string? retailerCode)
    {
        var request = ValidRequest() with { RetailerCode = retailerCode! };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("retailerCode", error.Message, StringComparison.Ordinal);
    }

    /// <summary>review D5 (round 2) — <c>asyncapi.yaml</c>'s <c>required</c> set is <c>[retailerCode, companyCode, currency, lines]</c>; <c>companyCode</c> had no case at all until now.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_AMissingOrBlankCompanyCode_Refuses(string? companyCode)
    {
        var request = ValidRequest() with { CompanyCode = companyCode! };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("companyCode", error.Message, StringComparison.Ordinal);
    }

    /// <summary>review D5 (round 2) — the fourth of the four required fields; also had no case.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_AMissingOrBlankCurrency_Refuses(string? currency)
    {
        var request = ValidRequest() with { Currency = currency! };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("currency", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ALineWithNoProductCode_Refuses()
    {
        var request = ValidRequest() with { Lines = [new OrdersCreateRequestLine("", 1, null, null)] };

        var error = Assert.Throws<InvalidOrdersCreateRequestError>(() => OrdersCreateRequestValidator.Validate(request));
        Assert.Contains("productCode", error.Message, StringComparison.Ordinal);
    }
}
