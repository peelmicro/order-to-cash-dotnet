using System.Text.RegularExpressions;
using OrderToCash.Gateway.Application.Rpc;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Fix round, review defect D1 — <c>GatewaySubjects</c> is derived from the
/// spec, never retyped: reads <c>specs/shared/asyncapi.yaml</c> as TEXT
/// (the <c>tests/Fulfillment.UnitTests/StockSubjectsTests.cs</c>/
/// <c>tests/Orders.UnitTests/RpcSubjectsTests.cs</c> block-extraction
/// discipline) and extracts each of the Gateway's eight channels' own
/// <c>address:</c> line. Before this file, the Gateway was the only
/// service in this repository that called RPC subjects with no
/// asyncapi-derived guard — every test that named a subject compared
/// <c>GatewaySubjects.X</c> to itself, so a corrupted constant left the
/// full unit and integration suites green (review probe P1: three of the
/// eight corrupted, 115/115 and 26/26 both stayed green).
/// </summary>
public sealed partial class GatewaySubjectsTests
{
    [Theory]
    [InlineData("ordersCreate", GatewaySubjects.OrdersCreate)]
    [InlineData("ordersCancel", GatewaySubjects.OrdersCancel)]
    [InlineData("catalogReferenceList", GatewaySubjects.CatalogReferenceList)]
    [InlineData("stockList", GatewaySubjects.StockList)]
    [InlineData("stockReplenish", GatewaySubjects.StockReplenish)]
    [InlineData("creditList", GatewaySubjects.CreditList)]
    [InlineData("invoiceList", GatewaySubjects.InvoiceList)]
    [InlineData("paymentRegister", GatewaySubjects.PaymentRegister)]
    public void GatewaySubjects_EqualTheAsyncApiChannelAddress(string channelKey, string expectedSubject)
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
