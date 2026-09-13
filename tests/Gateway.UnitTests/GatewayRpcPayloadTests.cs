using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Gateway.Application.Rpc;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Fix round, review defect D1 — every one of the Gateway's own request and
/// reply payload records carries EXACTLY the property names
/// <c>specs/shared/asyncapi.yaml</c> declares for its matching schema,
/// parsed from the spec via reflection over the record's own properties
/// (never a hand-typed key list — the shape
/// <c>tests/Billing.UnitTests/CreditRpcPayloadTests.cs</c> establishes,
/// chosen specifically so a transcription slip cannot be "confirmed"
/// against a second, independently wrong, hand-typed copy — backlog id 64.
/// <c>StockRpcPayloadTests</c>, <c>OrdersCancelPayloadTests</c> and
/// <c>CatalogReferenceListPayloadTests</c> kept the retyped-list shape
/// until backlog id 70 converted all three; every payload-key theory in the
/// solution now reads the record). Before
/// this file, six of the Gateway's eight subjects (every one except
/// <c>fulfillment.stock.list</c>/<c>fulfillment.stock.replenish</c>, which
/// <c>FulfillmentStockEndToEndTests</c> exercises against a real
/// responder) had their payload shape checked only by a stub the
/// implementer wrote to match its own assumption.
/// </summary>
public sealed class GatewayRpcPayloadTests
{
    /// <summary>
    /// Schemas whose Gateway record property set must equal the spec's
    /// exactly. <c>PageInfo</c> is declared ONCE in the spec and reused by
    /// all three list replies; the Gateway declares it three times
    /// (<see cref="StockPageInfo"/>, <see cref="CreditPageInfo"/>,
    /// <see cref="InvoicePageInfo"/>) rather than share a type across
    /// unrelated subjects, so it appears three times here too.
    /// </summary>
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "OrdersCreateRequestPayload", typeof(OrdersCreateRequestPayload) },
        { "OrdersCreateReplyPayload", typeof(OrdersCreateReplyPayload) },
        { "OrdersCancelRequestPayload", typeof(OrdersCancelRequestPayload) },
        { "OrdersCancelReplyPayload", typeof(OrdersCancelReplyPayload) },
        { "CatalogReferenceListRequestPayload", typeof(CatalogReferenceListRequestPayload) },
        { "CatalogReferenceListReplyPayload", typeof(CatalogReferenceListReplyPayload) },
        { "Product", typeof(ProductPayload) },
        { "Party", typeof(PartyPayload) },
        { "CurrencyView", typeof(CurrencyViewPayload) },
        { "StockListRequestPayload", typeof(StockListRequestPayload) },
        { "StockView", typeof(StockViewPayload) },
        { "StockListReplyPayload", typeof(StockListReplyPayload) },
        { "PageInfo", typeof(StockPageInfo) },
        { "StockReplenishRequestPayload", typeof(StockReplenishRequestPayload) },
        { "StockReplenishReplyPayload", typeof(StockReplenishReplyPayload) },
        { "CreditListRequestPayload", typeof(CreditListRequestPayload) },
        { "CreditView", typeof(CreditViewPayload) },
        { "CreditListReplyPayload", typeof(CreditListReplyPayload) },
        { "PageInfo", typeof(CreditPageInfo) },
        { "InvoiceListRequestPayload", typeof(InvoiceListRequestPayload) },
        { "InvoiceLine", typeof(InvoiceLine) },
        { "InvoiceListReplyPayload", typeof(GatewayInvoiceListReplyPayload) },
        { "PageInfo", typeof(InvoicePageInfo) },
        { "Money", typeof(CreditMoney) },
        { "PaymentRegisterRequestPayload", typeof(PaymentRegisterRequestPayload) },
        { "PaymentRegisterReplyPayload", typeof(PaymentRegisterReplyPayload) },
    };

    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties(schemaName, payloadType);
    }

    /// <summary>Backlog id 70, bullet 3 — the schema NAME each row above claims is guarded too, not only its key set.</summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void GatewayPayload_EveryRowsSchemaNameNamesTheRecordThatRowClaims(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertTheRowsSchemaNameNamesTheRecordItClaims(schemaName, payloadType);
    }

    /// <summary>
    /// <c>GatewayInvoiceViewPayload</c> is the one deliberate exception
    /// (review D8, "a comment, not a change"; backlog id 84's second and
    /// fifth acceptance bullets, which is why it now carries the
    /// <c>Gateway</c> prefix that says so at every use site): it carries an
    /// extra <c>lines</c>
    /// member Billing's own responder never sends and
    /// <c>asyncapi.yaml</c>'s <c>InvoiceView</c> does not declare — legal
    /// because <c>openapi.yaml</c>'s <c>Invoice</c> schema DOES declare
    /// an optional <c>lines</c>, and the property round-trips to
    /// <c>null</c>/omitted rather than reaching the wire. So the spec's own
    /// property set must be a SUBSET of the type's, with the singleton
    /// extra being exactly <c>lines</c> — never asserted as an exact match,
    /// unlike every other schema above.
    /// </summary>
    [Fact]
    public void GatewayInvoiceViewPayload_CarriesEveryAsyncApiPropertyPlusTheDocumentedOptionalLinesExtra()
    {
        var expected = AsyncApiSchema.PropertyNamesOf("InvoiceView").ToHashSet(StringComparer.Ordinal);
        var actual = typeof(GatewayInvoiceViewPayload).GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

        Assert.Subset(actual, expected);
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "lines" }, actual.Except(expected).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with
    /// <c>PaymentRegisterReplyPayload</c>'s <c>orderReference</c> property
    /// renamed: parsing the scratch copy no longer agrees with
    /// <see cref="PaymentRegisterReplyPayload"/>'s actual property set —
    /// exactly what would make the real
    /// <see cref="GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped"/>
    /// case fail if the spec really changed this way.
    /// </summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhosePaymentRegisterReplyPayloadPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-gateway-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace(
                    "        paymentReference:\n          $ref: '#/components/schemas/PaymentReference'\n        invoiceReference:\n          $ref: '#/components/schemas/InvoiceReference'\n        orderReference:",
                    "        paymentReference:\n          $ref: '#/components/schemas/PaymentReference'\n        invoiceReference:\n          $ref: '#/components/schemas/InvoiceReference'\n        orderReferenceRenamed:",
                    StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "PaymentRegisterReplyPayload").ToHashSet(StringComparer.Ordinal);
            var actual = typeof(PaymentRegisterReplyPayload).GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

            Assert.NotEqual(actual, parsedFromScratch);
            Assert.DoesNotContain("orderReference", parsedFromScratch);
            Assert.Contains("orderReferenceRenamed", parsedFromScratch);
        }
        finally
        {
            File.Delete(scratchPath);
        }
    }

    private static string ToCamelCase(System.Reflection.PropertyInfo property) =>
        string.Concat(char.ToLowerInvariant(property.Name[0]).ToString(), property.Name[1..]);
}
