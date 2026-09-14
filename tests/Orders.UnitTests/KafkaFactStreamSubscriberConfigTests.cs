using System.Reflection;
using Confluent.Kafka;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// SO9 — reads the private <c>BuildConsumerConfig</c> BY REFLECTION, the
/// same method the runtime calls, never a re-declared copy — the shape
/// <c>tests/Notifications.UnitTests/KafkaFactStreamSubscriberConfigTests.cs</c>
/// and <c>tests/Projector.UnitTests/KafkaFactStreamSubscriberConfigTests.cs</c>
/// both already establish (D3, feature 23 review round 1; PR38). Orders'
/// own <see cref="KafkaFactStreamSubscriber"/> was the ORIGINAL of the two —
/// design.md §3.1-§3.3 is where <c>EnableAutoOffsetStore = false</c> and
/// <c>AutoOffsetReset.Earliest</c> were first decided — but until this file
/// it was the one fact-consuming service whose config-building method had
/// no reflection-based unit guard of its own: only the container-level
/// <c>SagaConsumptionTests.SO9</c> exercised it, and only end to end.
/// Found and ported during backlog id 94 (batch 2), which noticed the gap
/// while arming Projector's <c>PR38</c> sibling against the same mutation
/// family.
/// </summary>
public sealed class KafkaFactStreamSubscriberConfigTests
{
    [Fact]
    public void SO9_BuildConsumerConfig_SetsEarliestNotLatest_SoAFreshConsumerGroupDoesNotSkipTheBacklog()
    {
        var config = BuildConfig();
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }

    [Fact]
    public void SO9_UsesTheOrdersSagaGroupAndClientIdentity_AndStoresOffsetsOnlyAfterTheHandler()
    {
        var config = BuildConfig();

        // GroupId is also ConsumerNames.ToToken(ConsumerName.OrdersSaga) —
        // one value for both the broker-side group identity and the
        // durable-ledger's dedup identity (the class's own remarks); a
        // drift here would silently split the two.
        Assert.Equal("orders.saga", config.GroupId);
        Assert.Equal("otc-orders-saga", config.ClientId);
        Assert.True(config.EnableAutoCommit, $"EnableAutoCommit was {config.EnableAutoCommit} — SO9 relies on the background committer to commit STORED offsets.");
        Assert.False(config.EnableAutoOffsetStore, $"EnableAutoOffsetStore was {config.EnableAutoOffsetStore} — SO9 requires the offset to be stored ONLY after the handler completes, never by the library's own at-most-once default.");
        Assert.False(config.EnablePartitionEof, $"EnablePartitionEof was {config.EnablePartitionEof} — this consumer never reads partition-EOF sentinels.");
    }

    private static ConsumerConfig BuildConfig()
    {
        var method = typeof(KafkaFactStreamSubscriber).GetMethod("BuildConsumerConfig", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("KafkaFactStreamSubscriber.BuildConsumerConfig not found by reflection.");

        var options = new OrdersSagaOptions();
        options.Kafka.BootstrapServers = "localhost:9092";
        options.Kafka.PollTimeoutMs = 500;

        return (ConsumerConfig)(method.Invoke(null, [options])
            ?? throw new InvalidOperationException("BuildConsumerConfig returned null."));
    }
}
