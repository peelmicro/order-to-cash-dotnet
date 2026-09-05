using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>One case per rule of design.md §6.4 — ledger L11.</summary>
public sealed class StockRequestValidatorTests
{
    [Fact]
    public void ValidateReserve_AcceptsAWellFormedRequest()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 3)]);
        StockRequestValidator.ValidateReserve(request); // must not throw
    }

    [Theory]
    [InlineData("ORD-1")] // too few digits
    [InlineData("ORDER-000001")]
    [InlineData("")]
    public void ValidateReserve_RejectsAMalformedOrderReference(string orderReference)
    {
        var request = new StockReserveRequestPayload(orderReference, "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 3)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsAnEmptyRetailerCode()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "", "ACME", [new StockReserveRequestLine("P1", 3)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsACompanyCodeLongerThanTwentyCharacters()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", new string('A', 21), [new StockReserveRequestLine("P1", 3)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsAProductCodeLongerThanThirtyCharacters()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine(new string('A', 31), 3)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsANonPositiveUnitsValue()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 0)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsAnEmptyLinesList()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", []);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Fact]
    public void ValidateReserve_RejectsANonAsciiProductCode()
    {
        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("café", 3)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReserve(request));
    }

    [Theory]
    [InlineData("credit_rejected")]
    [InlineData("order_cancelled")]
    public void ValidateRelease_AcceptsBothDeclaredReasons(string reason)
    {
        var request = new StockReleaseRequestPayload("ORD-000001", reason);
        StockRequestValidator.ValidateRelease(request);
    }

    [Fact]
    public void ValidateRelease_RejectsAnUndeclaredReason()
    {
        var request = new StockReleaseRequestPayload("ORD-000001", "not_a_real_reason");
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateRelease(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateList_RejectsAPageBelowOne(int page)
    {
        var request = new StockListRequestPayload(page, 25);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateList(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ValidateList_RejectsAPageSizeOutsideOneToTwoHundred(int pageSize)
    {
        var request = new StockListRequestPayload(1, pageSize);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateList(request));
    }

    [Fact]
    public void ValidateList_AppliesNoDefaultingItself_LeavingNullPageAndPageSizeValid()
    {
        var request = new StockListRequestPayload(null, null);
        StockRequestValidator.ValidateList(request); // defaults are applied downstream, at the read repository
    }

    [Fact]
    public void ValidateReplenish_RejectsAnUnknownProductCodeLength()
    {
        var request = new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("", 5)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReplenish(request));
    }

    [Fact]
    public void ValidateReplenish_RejectsANonPositiveUnitsValue()
    {
        var request = new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("P1", -1)]);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateReplenish(request));
    }

    [Fact]
    public void ValidateCheck_RejectsAnEmptyLinesList()
    {
        var request = new StockCheckRequestPayload("ACME", []);
        Assert.Throws<InvalidStockRequestError>(() => StockRequestValidator.ValidateCheck(request));
    }
}
