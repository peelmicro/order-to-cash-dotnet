using System.Text.RegularExpressions;
using OrderToCash.Billing.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI16` half — <see cref="InvoiceSubjects"/>' two constants equal their
/// `specs/shared/asyncapi.yaml` channel `address` values character for
/// character, read from the spec as text, never retyped.
/// </summary>
public sealed partial class InvoiceSubjectsTests
{
    [Theory]
    [InlineData("invoiceIssue", InvoiceSubjects.InvoiceIssue)]
    [InlineData("invoiceList", InvoiceSubjects.InvoiceList)]
    public void InvoiceSubjects_EqualTheAsyncApiChannelAddress(string channelKey, string expectedSubject)
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
