using System.Reflection;
using Confluent.Kafka;
using OrderToCash.Projector.Infrastructure;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>PR38</c> — reads the private <c>BuildConsumerConfig</c> BY REFLECTION,
/// the same method the runtime calls (the Notifications D3 shape), never a
/// re-declared copy. Armed by changing <c>AutoOffsetReset.Earliest</c> to
/// <c>Latest</c> AT THE ASSIGNMENT SITE; the mutation-applied SHA-256
/// before/after proof lives in <c>progress/impl_projector_read_model.md</c>
/// row K8, not as a test in this file (review defect D2(b)).
/// </summary>
public sealed class KafkaFactStreamSubscriberConfigTests
{
    [Fact]
    public void PR38_BuildConsumerConfig_SetsEarliestNotLatest_BecauseTheReadModelIsRebuiltByReplay()
    {
        var config = BuildConfig();
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }

    [Fact]
    public void PR38_UsesTheProjectorGroupAndClientIdentity_AndStoresOffsetsOnlyAfterTheHandler()
    {
        var config = BuildConfig();

        Assert.Equal("projector", config.GroupId);
        Assert.Equal("otc-projector", config.ClientId);
        Assert.True(config.EnableAutoCommit);
        Assert.False(config.EnableAutoOffsetStore);
        Assert.False(config.EnablePartitionEof);
    }

    // A test that hashed the same file twice and compared the two hashes to
    // itself used to live here, captioned as "the mutation-applied proof
    // this method's own arming table needs." It could never fail — a
    // before/after mutation-applied proof takes two READS ACROSS A REAL
    // MUTATION, recorded in the implementer's arming table
    // (progress/impl_projector_read_model.md, row K8: SHA-256
    // 52a8eb7c…7fe284 before, d341607…418b79 after AutoOffsetReset.Earliest
    // was changed to Latest at the assignment site), not two reads of one
    // unmutated file inside a test. Removed in the fix round for review
    // defect D2(b) rather than given a new claim to make, because the
    // property it gestured at (PR38's config assignment is what the runtime
    // actually reads) is already proven by PR38_BuildConsumerConfig_Sets…
    // above, which reads BuildConsumerConfig by reflection — the same
    // method the runtime calls — and fails for real when the assignment is
    // mutated (see the arming table).

    private static ConsumerConfig BuildConfig()
    {
        var method = typeof(KafkaFactStreamSubscriber).GetMethod("BuildConsumerConfig", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("KafkaFactStreamSubscriber.BuildConsumerConfig not found by reflection.");

        var options = new ProjectorKafkaOptions { BootstrapServers = "localhost:9092", PollTimeoutMs = 500 };
        return (ConsumerConfig)method.Invoke(null, [options])!;
    }
}
