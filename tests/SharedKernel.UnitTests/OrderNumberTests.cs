using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>domain-model.md §2.3 — the `ORD-000001` business-reference shape. Not R-numbered; general coverage.</summary>
public sealed class OrderNumberTests
{
    [Fact]
    public void OrderNumber_FormatsASequenceAsAZeroPaddedSixDigitReference()
    {
        var orderNumber = new OrderNumber(1);

        Assert.Equal("ORD-000001", orderNumber.Value);
    }

    [Fact]
    public void OrderNumber_GrowsBeyondSixDigitsRatherThanTruncating()
    {
        var orderNumber = new OrderNumber(1_234_567);

        Assert.Equal("ORD-1234567", orderNumber.Value);
    }

    [Fact]
    public void OrderNumber_RejectsAZeroOrNegativeSequence()
    {
        Assert.Throws<InvalidOrderNumberError>(() => new OrderNumber(0));
        Assert.Throws<InvalidOrderNumberError>(() => new OrderNumber(-1));
    }

    [Fact]
    public void OrderNumber_ParseRoundTripsAWellFormedReferenceAndRejectsAMalformedOne()
    {
        var parsed = OrderNumber.Parse("ORD-000042");
        Assert.Equal("ORD-000042", parsed.Value);

        Assert.Throws<InvalidOrderNumberError>(() => OrderNumber.Parse("INV-000042"));
        Assert.Throws<InvalidOrderNumberError>(() => OrderNumber.Parse("ORD-42"));
        Assert.Throws<InvalidOrderNumberError>(() => OrderNumber.Parse("ORD-00004A"));
    }
}
