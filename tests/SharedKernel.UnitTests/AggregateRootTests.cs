using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>CLAUDE.md — "AggregateRoot ... collects domain events." Not R-numbered; general coverage.</summary>
public sealed class AggregateRootTests
{
    private sealed record TestDomainEvent(string What) : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot
    {
        public TestAggregate(UniqueId id)
            : base(id)
        {
        }

        public void DoSomethingThatRaisesAnEvent() => Raise(new TestDomainEvent("something happened"));
    }

    [Fact]
    public void AggregateRoot_StartsWithNoPendingDomainEvents()
    {
        var aggregate = new TestAggregate(UniqueId.New());

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void AggregateRoot_CollectsRaisedDomainEventsInOrder()
    {
        var aggregate = new TestAggregate(UniqueId.New());

        aggregate.DoSomethingThatRaisesAnEvent();
        aggregate.DoSomethingThatRaisesAnEvent();

        Assert.Equal(2, aggregate.DomainEvents.Count);
        Assert.All(aggregate.DomainEvents, e => Assert.IsType<TestDomainEvent>(e));
    }

    [Fact]
    public void AggregateRoot_ClearDomainEventsEmptiesThePendingList()
    {
        var aggregate = new TestAggregate(UniqueId.New());
        aggregate.DoSomethingThatRaisesAnEvent();

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }
}
