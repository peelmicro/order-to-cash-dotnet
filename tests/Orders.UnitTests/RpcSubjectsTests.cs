using System.Text.RegularExpressions;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>RpcSubjects</c> is derived from the spec, never retyped — reads
/// <c>specs/shared/asyncapi.yaml</c> as TEXT (the same discipline
/// <c>OrdersFactTopicTests</c> already follows for the Kafka topic) and
/// extracts the <c>ordersCreate</c>/<c>stockCheck</c> channels' own
/// <c>address:</c> lines.
/// </summary>
public sealed partial class RpcSubjectsTests
{
    [Fact]
    public void RpcSubjects_OrdersCreate_EqualsTheAsyncApiOrdersCreateChannelAddress()
    {
        var address = ReadChannelAddress("ordersCreate");
        Assert.Equal(RpcSubjects.OrdersCreate, address);
    }

    [Fact]
    public void RpcSubjects_StockCheck_EqualsTheAsyncApiStockCheckChannelAddress()
    {
        var address = ReadChannelAddress("stockCheck");
        Assert.Equal(RpcSubjects.StockCheck, address);
    }

    private static string ReadChannelAddress(string channelKey)
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        // The channel block runs from its own "  <channelKey>:" line up to
        // (but not including) the next sibling channel key at the same
        // two-space indent.
        var channelMatch = new Regex($@"  {Regex.Escape(channelKey)}:.*?(?=\n  \w+:)", RegexOptions.Singleline).Match(spec);
        Assert.True(channelMatch.Success, $"could not locate the '{channelKey}:' channel block in specs/shared/asyncapi.yaml");

        var addressMatch = AddressRegex().Match(channelMatch.Value);
        Assert.True(addressMatch.Success, $"the '{channelKey}' channel block has no 'address: <value>' line");

        return addressMatch.Groups[1].Value;
    }

    [GeneratedRegex(@"address:\s*(\S+)")]
    private static partial Regex AddressRegex();
}
