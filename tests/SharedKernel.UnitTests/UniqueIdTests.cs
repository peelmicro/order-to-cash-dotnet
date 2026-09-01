using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>domain-model.md §2.5. Not R-numbered; general coverage.</summary>
public sealed class UniqueIdTests
{
    [Fact]
    public void UniqueId_NewGeneratesADistinctNonEmptyIdentityEachTime()
    {
        var first = UniqueId.New();
        var second = UniqueId.New();

        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void UniqueId_TwoIdsAreEqualIffTheirUnderlyingValuesAreEqual()
    {
        var guid = Guid.NewGuid();

        var left = UniqueId.From(guid);
        var right = UniqueId.From(guid);

        Assert.Equal(left, right);
        Assert.True(left == right);
    }

    [Fact]
    public void UniqueId_FromRejectsAnEmptyGuid()
    {
        Assert.Throws<InvalidUniqueIdError>(() => UniqueId.From(Guid.Empty));
    }
}
