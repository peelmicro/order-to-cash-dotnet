using System.Text.RegularExpressions;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.3 — the topic constant is derived from the spec, never
/// retyped: reads <c>specs/shared/asyncapi.yaml</c> as TEXT (no YAML parser
/// package, resolving the repository root the way
/// <c>tests/Contracts.UnitTests/RepositoryPaths.cs</c> already does) and
/// extracts the <c>ordersFacts</c> channel's <c>bindings.kafka.topic</c>.
/// </summary>
public sealed partial class OrdersFactTopicTests
{
    [Fact]
    public void OrdersFactTopic_EqualsTheTopicTheAsyncApiOrdersFactsChannelDeclares()
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        // The ordersFacts channel block runs from its own "  ordersFacts:"
        // line up to (but not including) the next sibling channel key at the
        // same two-space indent.
        var channelMatch = ChannelBlockRegex().Match(spec);
        Assert.True(channelMatch.Success, "could not locate the 'ordersFacts:' channel block in specs/shared/asyncapi.yaml");

        var topicMatch = TopicRegex().Match(channelMatch.Value);
        Assert.True(topicMatch.Success, "the 'ordersFacts' channel block has no 'topic: <value>' line under bindings.kafka");

        Assert.Equal(OrdersFactTopic.Name, topicMatch.Groups[1].Value);
    }

    [GeneratedRegex(@"  ordersFacts:.*?(?=\n  \w+:)", RegexOptions.Singleline)]
    private static partial Regex ChannelBlockRegex();

    [GeneratedRegex(@"topic:\s*(\S+)")]
    private static partial Regex TopicRegex();
}
