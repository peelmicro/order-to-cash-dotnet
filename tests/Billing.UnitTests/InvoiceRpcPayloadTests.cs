using System.Text.Json;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Contracts.Facts;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI28` — every `billing.invoice.*` request and reply record carries
/// EXACTLY the property names `asyncapi.yaml` declares, parsed from the
/// spec, never retyped. Also: an `issued` <see cref="InvoiceViewPayload"/>
/// OMITS the `paidAt` key entirely; a `paid` one carries it (§4.3, ledger
/// `L19`).
/// </summary>
public sealed class InvoiceRpcPayloadTests
{
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "InvoiceIssueRequestPayload", typeof(InvoiceIssueRequestPayload) },
        { "InvoiceIssueReplyPayload", typeof(InvoiceIssueReplyPayload) },
        { "InvoiceListRequestPayload", typeof(InvoiceListRequestPayload) },
        { "InvoiceListReplyPayload", typeof(InvoiceListReplyPayload) },
        { "InvoiceView", typeof(InvoiceViewPayload) },
        { "PageInfo", typeof(InvoicePageInfo) },
        { "InvoiceLine", typeof(InvoiceLine) },
        // Feature 22.
        { "PaymentRegisterRequestPayload", typeof(PaymentRegisterRequestPayload) },
        { "PaymentRegisterReplyPayload", typeof(PaymentRegisterReplyPayload) },
    };

    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BI28_EveryInvoiceRequestAndReplyRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(string schemaName, Type payloadType)
    {
        var expected = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var actual = payloadType.GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BI28_AnIssuedInvoiceViewOmitsThePaidAtKeyEntirely_AndAPaidOneCarriesIt()
    {
        var issued = new InvoiceViewPayload(Guid.NewGuid(), "INV-000001", DateTimeOffset.UtcNow, "ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", 1_000, 0, 1_000, "issued");
        var issuedJson = JsonSerializer.SerializeToElement(issued, OrderToCash.Contracts.Wire.JsonWire.Options);
        var issuedKeys = issuedJson.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("paidAt", issuedKeys);
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "invoiceId", "invoiceReference", "invoiceDate", "orderReference", "retailerCode", "companyCode", "currency", "amount", "discount", "totalAmount", "status" }, issuedKeys);

        var paid = issued with { Status = "paid", PaidAt = DateTimeOffset.UtcNow };
        var paidJson = JsonSerializer.SerializeToElement(paid, OrderToCash.Contracts.Wire.JsonWire.Options);
        var paidKeys = paidJson.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("paidAt", paidKeys);
    }

    /// <summary>ARM 1 — against a SCRATCH copy of the spec (never the real, read-only file), rename one <see cref="InvoiceIssueReplyPayload"/> property and confirm the parity assertion no longer agrees.</summary>
    [Fact]
    public void ArmedAgainstAScratchCopy_TheGuardFailsWhenInvoiceIssueReplyPayloadPropertyIsRenamedInTheSpec()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchText = File.ReadAllText(realSpecPath).Replace(
            "    InvoiceIssueReplyPayload:\n      type: object\n      properties:\n        orderReference:",
            "    InvoiceIssueReplyPayload:\n      type: object\n      properties:\n        orderReferenceRenamed:",
            StringComparison.Ordinal);

        var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "InvoiceIssueReplyPayload").ToHashSet(StringComparer.Ordinal);
        var actual = typeof(InvoiceIssueReplyPayload).GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

        Assert.NotEqual(actual, parsedFromScratch);
        Assert.DoesNotContain("orderReference", parsedFromScratch);
        Assert.Contains("orderReferenceRenamed", parsedFromScratch);
    }

    private static string ToCamelCase(System.Reflection.PropertyInfo property) =>
        string.Concat(char.ToLowerInvariant(property.Name[0]).ToString(), property.Name[1..]);
}
