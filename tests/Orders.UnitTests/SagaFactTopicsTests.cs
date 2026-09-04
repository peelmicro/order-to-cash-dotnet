using System.Text.RegularExpressions;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §3.2/§10.1 — the three consumed topic constants are derived
/// from the spec, never retyped: reads <c>specs/shared/asyncapi.yaml</c> as
/// TEXT (the same discipline <c>OrdersFactTopicTests</c> already follows)
/// and extracts <c>ordersFacts</c>/<c>fulfillmentFacts</c>/<c>billingFacts</c>'
/// own <c>bindings.kafka.topic</c> line.
/// </summary>
public sealed partial class SagaFactTopicsTests
{
    [Fact]
    public void SagaFactTopics_EqualTheTopicsTheAsyncApiFactChannelsDeclare()
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        Assert.Equal(SagaFactTopics.OrdersFacts, ReadChannelTopic(spec, "ordersFacts"));
        Assert.Equal(SagaFactTopics.FulfillmentFacts, ReadChannelTopic(spec, "fulfillmentFacts"));
        Assert.Equal(SagaFactTopics.BillingFacts, ReadChannelTopic(spec, "billingFacts"));
    }

    private static string ReadChannelTopic(string spec, string channelKey)
    {
        // The channel block runs from its own "  <channelKey>:" line up to
        // (but not including) the next sibling channel key at the same
        // two-space indent — OrdersFactTopicTests' own discipline.
        var channelMatch = new Regex($@"  {Regex.Escape(channelKey)}:.*?(?=\n  \w+:)", RegexOptions.Singleline).Match(spec);
        Assert.True(channelMatch.Success, $"could not locate the '{channelKey}:' channel block in specs/shared/asyncapi.yaml");

        var topicMatch = TopicRegex().Match(channelMatch.Value);
        Assert.True(topicMatch.Success, $"the '{channelKey}' channel block has no 'topic: <value>' line under bindings.kafka");

        return topicMatch.Groups[1].Value;
    }

    [GeneratedRegex(@"topic:\s*(\S+)")]
    private static partial Regex TopicRegex();
}
