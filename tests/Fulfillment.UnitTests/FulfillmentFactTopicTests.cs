using System.Text.RegularExpressions;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The topic constant is derived from the spec, never retyped — reads
/// <c>specs/shared/asyncapi.yaml</c> as TEXT and extracts the
/// <c>fulfillmentFacts</c> channel's own <c>address:</c> value (the same
/// discipline <c>OrdersFactTopicTests</c> follows for Orders' topic).
/// </summary>
public sealed partial class FulfillmentFactTopicTests
{
    [Fact]
    public void FulfillmentFactTopic_EqualsTheAsyncApiFulfillmentFactsChannelAddress()
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        var channelMatch = ChannelBlockRegex().Match(spec);
        Assert.True(channelMatch.Success, "could not locate the 'fulfillmentFacts:' channel block in specs/shared/asyncapi.yaml");

        var addressMatch = AddressRegex().Match(channelMatch.Value);
        Assert.True(addressMatch.Success, "the 'fulfillmentFacts' channel block has no 'address: <value>' line");

        Assert.Equal(FulfillmentFactTopic.Name, addressMatch.Groups[1].Value);
    }

    [GeneratedRegex(@"  fulfillmentFacts:.*?(?=\n  \w+:)", RegexOptions.Singleline)]
    private static partial Regex ChannelBlockRegex();

    [GeneratedRegex(@"address:\s*(\S+)")]
    private static partial Regex AddressRegex();
}
