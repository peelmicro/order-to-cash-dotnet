using System.Reflection;
using Confluent.Kafka;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// D3 (feature 23 review round 1) — the ported-idiom ledger entry this
/// feature owns (<see cref="KafkaFactStreamSubscriber"/>'s own class
/// remarks: "why AutoOffsetReset.Latest, the OPPOSITE of Orders'
/// AutoOffsetReset.Earliest, and not an oversight") was nominated in
/// <c>progress/impl_notifications_service.md</c> but asserted by nothing —
/// before this file, flipping the single token to <c>Earliest</c> restored
/// #7's live mail-storm exposure on a fully green solution. Reaches
/// <c>BuildConsumerConfig</c> via reflection rather than widening its
/// visibility: the method is deliberately private (nothing outside this one
/// class needs it), and reflection reads the SAME private method the
/// runtime calls, not a copy that could itself drift from it.
/// </summary>
public sealed class KafkaFactStreamSubscriberConfigTests
{
    [Fact]
    public void BuildConsumerConfig_SetsLatestNotEarliest_SoAFreshConsumerGroupDoesNotMailTheHistoricalBacklog()
    {
        var config = BuildConsumerConfig();

        Assert.Equal(AutoOffsetReset.Latest, config.AutoOffsetReset);
    }

    [Fact]
    public void BuildConsumerConfig_UsesTheNotificationsConsumerGroupAndClientIdentity()
    {
        var config = BuildConsumerConfig();

        // GroupId is also the durable-ledger's ConsumerName token
        // (KafkaFactStreamSubscriber's own remarks) — a drift here would
        // silently split the broker-side group identity from the ledger's
        // dedup identity.
        Assert.Equal("notifications", config.GroupId);
        Assert.Equal("otc-notifications", config.ClientId);
    }

    [Fact]
    public void BuildConsumerConfig_StoresOffsetsOnlyAfterTheHandlerRuns()
    {
        var config = BuildConsumerConfig();

        Assert.True(config.EnableAutoCommit);
        Assert.False(config.EnableAutoOffsetStore);
    }

    private static ConsumerConfig BuildConsumerConfig()
    {
        var method = typeof(KafkaFactStreamSubscriber).GetMethod(
            "BuildConsumerConfig",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("KafkaFactStreamSubscriber.BuildConsumerConfig was not found — has it been renamed or made non-static?");

        var options = new NotificationsKafkaOptions { BootstrapServers = "test-broker:9092" };

        return (ConsumerConfig)(method.Invoke(null, [options])
            ?? throw new InvalidOperationException("BuildConsumerConfig returned null."));
    }
}
