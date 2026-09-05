using System.Text.RegularExpressions;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// <c>StockSubjects</c> is derived from the spec, never retyped — reads
/// <c>specs/shared/asyncapi.yaml</c> as TEXT (the discipline
/// <c>tests/Orders.UnitTests/RpcSubjectsTests.cs</c> already establishes) and
/// extracts each <c>fulfillment.stock.*</c> channel's own <c>address:</c>
/// line.
/// </summary>
public sealed partial class StockSubjectsTests
{
    [Theory]
    [InlineData("stockCheck", StockSubjects.StockCheck)]
    [InlineData("stockReserve", StockSubjects.StockReserve)]
    [InlineData("stockRelease", StockSubjects.StockRelease)]
    [InlineData("stockList", StockSubjects.StockList)]
    [InlineData("stockReplenish", StockSubjects.StockReplenish)]
    public void StockSubjects_EqualTheAsyncApiChannelAddress(string channelKey, string expectedSubject)
    {
        var address = ReadChannelAddress(channelKey);
        Assert.Equal(expectedSubject, address);
    }

    private static string ReadChannelAddress(string channelKey)
    {
        var specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var spec = File.ReadAllText(specPath);

        var channelMatch = new Regex($@"  {Regex.Escape(channelKey)}:.*?(?=\n  \w+:)", RegexOptions.Singleline).Match(spec);
        Assert.True(channelMatch.Success, $"could not locate the '{channelKey}:' channel block in specs/shared/asyncapi.yaml");

        var addressMatch = AddressRegex().Match(channelMatch.Value);
        Assert.True(addressMatch.Success, $"the '{channelKey}' channel block has no 'address: <value>' line");

        return addressMatch.Groups[1].Value;
    }

    [GeneratedRegex(@"address:\s*(\S+)")]
    private static partial Regex AddressRegex();
}
