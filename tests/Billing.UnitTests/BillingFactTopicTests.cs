using OrderToCash.Billing.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary><c>BillingFactTopic</c> is derived from the spec, never retyped — reads <c>specs/shared/asyncapi.yaml</c> as text and extracts the <c>billingFacts</c> channel's <c>bindings.kafka.topic</c>.</summary>
public sealed class BillingFactTopicTests
{
    [Fact]
    public void BillingFactTopic_EqualsTheAsyncApiBillingFactsChannelsKafkaTopicBinding()
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        var channelIndex = spec.IndexOf("\n  billingFacts:\n", StringComparison.Ordinal);
        Assert.True(channelIndex >= 0, "could not locate the 'billingFacts:' channel block in specs/shared/asyncapi.yaml");

        var nextChannelIndex = spec.IndexOf("\n  ordersFactsDlq:", channelIndex, StringComparison.Ordinal);
        Assert.True(nextChannelIndex > channelIndex, "could not find the end of the 'billingFacts' channel block");

        var block = spec[channelIndex..nextChannelIndex];

        var topicIndex = block.IndexOf("topic: ", StringComparison.Ordinal);
        Assert.True(topicIndex >= 0, "the 'billingFacts' channel block has no 'topic:' binding");

        var topicLine = block[(topicIndex + "topic: ".Length)..];
        var topic = topicLine[..topicLine.IndexOf('\n')].Trim();

        Assert.Equal(BillingFactTopic.Name, topic);
    }
}
