using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`B2` — <see cref="CreditLedgerEntry"/> is append-only by shape, and `Create` refuses a non-positive amount. `BC12`'s token half — `CreditEntryTypes.Parse` refuses anything outside the closed set.</summary>
public sealed class CreditLedgerEntryTests
{
    [Fact]
    public void Create_RefusesANonPositiveAmount()
    {
        Assert.Throws<InvalidBuyerCreditSnapshotError>(() =>
            CreditLedgerEntry.Create(UniqueId.New(), OrderNumber.Parse("ORD-000001"), new Money(0, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow));

        Assert.Throws<InvalidBuyerCreditSnapshotError>(() =>
            CreditLedgerEntry.Create(UniqueId.New(), OrderNumber.Parse("ORD-000001"), new Money(-1, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CreditEntryTypes_Parse_RaisesOnATokenOutsideTheClosedSet_RatherThanIgnoringTheRow()
    {
        Assert.Throws<UnknownCreditEntryTypeError>(() => CreditEntryTypes.Parse("refund"));
        Assert.Throws<UnknownCreditEntryTypeError>(() => CreditEntryTypes.Parse(null));
        Assert.Throws<UnknownCreditEntryTypeError>(() => CreditEntryTypes.Parse(string.Empty));
    }

    [Theory]
    [InlineData(CreditEntryType.Hold, "hold")]
    [InlineData(CreditEntryType.Consume, "consume")]
    [InlineData(CreditEntryType.Release, "release")]
    public void CreditEntryTypes_RoundTripsEveryClosedSetMember(CreditEntryType type, string token)
    {
        Assert.Equal(token, CreditEntryTypes.ToToken(type));
        Assert.Equal(type, CreditEntryTypes.Parse(token));
    }
}
