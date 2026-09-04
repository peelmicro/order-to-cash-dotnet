using System.Text.RegularExpressions;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §6.1 — the five saga command subjects are derived from the
/// spec, never retyped: reads <c>specs/shared/asyncapi.yaml</c> as TEXT (the
/// same discipline <c>RpcSubjectsTests</c> already follows) and extracts each
/// channel's own <c>address:</c> line. <c>RpcSubjectsTests</c> itself stays
/// untouched and green (tasks.md A4).
/// </summary>
public sealed partial class SagaRpcSubjectsTests
{
    [Theory]
    [InlineData("stockReserve", nameof(RpcSubjects.StockReserve))]
    [InlineData("stockRelease", nameof(RpcSubjects.StockRelease))]
    [InlineData("despatchCreate", nameof(RpcSubjects.DespatchCreate))]
    [InlineData("creditHold", nameof(RpcSubjects.CreditHold))]
    [InlineData("invoiceIssue", nameof(RpcSubjects.InvoiceIssue))]
    public void SagaRpcSubjects_EqualTheAddressesTheAsyncApiChannelsDeclare(string channelKey, string subjectConstantName)
    {
        var address = ReadChannelAddress(channelKey);
        var actual = subjectConstantName switch
        {
            nameof(RpcSubjects.StockReserve) => RpcSubjects.StockReserve,
            nameof(RpcSubjects.StockRelease) => RpcSubjects.StockRelease,
            nameof(RpcSubjects.DespatchCreate) => RpcSubjects.DespatchCreate,
            nameof(RpcSubjects.CreditHold) => RpcSubjects.CreditHold,
            nameof(RpcSubjects.InvoiceIssue) => RpcSubjects.InvoiceIssue,
            _ => throw new InvalidOperationException($"Unrecognised subject constant name '{subjectConstantName}'."),
        };

        Assert.Equal(address, actual);
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
