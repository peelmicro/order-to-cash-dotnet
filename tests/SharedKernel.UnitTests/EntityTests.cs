using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>CLAUDE.md — "Entity/AggregateRoot are classes with identity equality." Not R-numbered; general coverage.</summary>
public sealed class EntityTests
{
    private sealed class TestEntity(UniqueId id) : Entity(id);

    private sealed class OtherEntity(UniqueId id) : Entity(id);

    [Fact]
    public void Entity_TwoInstancesOfTheSameTypeWithTheSameIdAreEqual()
    {
        var id = UniqueId.New();

        var left = new TestEntity(id);
        var right = new TestEntity(id);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Entity_TwoInstancesWithDifferentIdsAreNotEqualEvenOfTheSameType()
    {
        var left = new TestEntity(UniqueId.New());
        var right = new TestEntity(UniqueId.New());

        Assert.NotEqual(left, right);
        Assert.True(left != right);
    }

    [Fact]
    public void Entity_EqualityIsByRuntimeTypeAsWellAsId_NotJustById()
    {
        var id = UniqueId.New();

        Entity left = new TestEntity(id);
        Entity right = new OtherEntity(id);

        Assert.NotEqual(left, right);
    }
}
