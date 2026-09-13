using System.Text.Json;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The <c>StockRpcPayloadTests</c>/<c>CreditRpcPayloadTests</c> instrument,
/// applied to <c>catalog.reference.list</c>'s own payload records: the
/// <c>[Fact]</c> cases serialise a real instance and assert the keys that
/// actually REACH the wire through the ONE shared
/// <see cref="JsonWire.Options"/> (camelCase, nulls omitted), and
/// <see cref="BC23_EveryCatalogReferenceListRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped"/>
/// asserts the CONTRACT — the record's own property set, read by
/// reflection, against the set parsed from
/// <c>specs/shared/asyncapi.yaml</c> (backlog id 70, which replaced this
/// file's hand-retyped key lists with that reading).
/// </summary>
public sealed class CatalogReferenceListPayloadTests
{
    [Fact]
    public void CatalogReferenceListRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new CatalogReferenceListRequestPayload(["products"], true);
        var json = RoundTrip(payload);

        AssertKeys(json, "kinds", "includeDisabled");
    }

    [Fact]
    public void CatalogReferenceListRequestPayload_OmitsAbsentOptionalsRatherThanEmittingNull()
    {
        var payload = new CatalogReferenceListRequestPayload(null, null);
        var json = RoundTrip(payload);

        AssertKeys(json);
    }

    [Fact]
    public void ProductPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new ProductPayload("PROD-001", "1000000000017", "Product One", "First product", 1_000, "EUR", true);
        var json = RoundTrip(payload);

        AssertKeys(json, "code", "ean", "name", "description", "price", "currency", "enabled");
    }

    [Fact]
    public void PartyPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new PartyPayload("RETAILER-01", "Test Retailer", "FR", "FR00000000000", "4006381333931", "EUR", true);
        var json = RoundTrip(payload);

        AssertKeys(json, "code", "name", "country", "vat", "gln", "currency", "enabled");
    }

    [Fact]
    public void CurrencyViewPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new CurrencyViewPayload("EUR", "978", "€", 2);
        var json = RoundTrip(payload);

        AssertKeys(json, "code", "isoNumber", "symbol", "decimalPoints");
    }

    [Fact]
    public void CatalogReferenceListReplyPayload_OnlyTheRequestedCollectionsArePresent()
    {
        var productsOnly = new CatalogReferenceListReplyPayload(
            Products: [new ProductPayload("PROD-001", "1000000000017", "Product One", "First product", 1_000, "EUR", true)],
            Retailers: null,
            Companies: null,
            Currencies: null);
        var json = RoundTrip(productsOnly);

        AssertKeys(json, "products");
        Assert.False(json.RootElement.TryGetProperty("retailers", out _));
        Assert.False(json.RootElement.TryGetProperty("companies", out _));
        Assert.False(json.RootElement.TryGetProperty("currencies", out _));
    }

    [Fact]
    public void CatalogReferenceListReplyPayload_AllFourCollectionsPresent_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new CatalogReferenceListReplyPayload(
            Products: [],
            Retailers: [],
            Companies: [],
            Currencies: []);
        var json = RoundTrip(payload);

        AssertKeys(json, "products", "retailers", "companies", "currencies");
    }

    /// <summary>
    /// The rows this file's two <c>BC23</c> theories below walk: an
    /// <c>asyncapi.yaml</c> schema name paired with the RECORD that claims
    /// it — <c>OrderToCash.Orders.Presentation.Rpc</c>'s, Orders being the
    /// service that ANSWERS <c>catalog.reference.list</c>. The Gateway's
    /// own caller-side copies of these five records
    /// (<c>src/Gateway/Application/Rpc/GatewayRpcPayloads.cs</c>, kept
    /// because <c>src/Contracts/Rpc</c> has no counterpart for this
    /// subject — backlog id 84) have their own rows in
    /// <c>tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs</c>.
    /// </summary>
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "CatalogReferenceListRequestPayload", typeof(CatalogReferenceListRequestPayload) },
        { "Product", typeof(ProductPayload) },
        { "Party", typeof(PartyPayload) },
        { "CurrencyView", typeof(CurrencyViewPayload) },
        { "CatalogReferenceListReplyPayload", typeof(CatalogReferenceListReplyPayload) },
    };

    /// <summary>
    /// Backlog id 51, `BC23`'s discipline — and backlog id 70, which
    /// retired this theory's previous HAND-RETYPED key list. It compared a
    /// literal array against the set parsed from
    /// <c>specs/shared/asyncapi.yaml</c> and never read the payload record,
    /// so an UNDECLARED property added to any of these five left the whole
    /// of <c>Orders.UnitTests</c> green: nulls are OMITTED by
    /// <see cref="JsonWire.Options"/>, so the serialised-key cases above
    /// could not see it either.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryCatalogReferenceListRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties(schemaName, payloadType);
    }

    /// <summary>
    /// Backlog id 70, bullet 3 — the schema NAME each row claims is
    /// guarded too, not only the key set. <c>Product</c> and <c>Party</c>
    /// are exactly the pair this needs: both declare seven keys, five of
    /// which are the same word, so transposing those two row labels is
    /// nearly invisible to a key-set comparison alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertTheRowsSchemaNameNamesTheRecordItClaims(schemaName, payloadType);
    }

    /// <summary>`G5` — the arming that proves the guard has teeth: a SCRATCH copy of the real spec (never the real, read-only <c>specs/shared/asyncapi.yaml</c>) with <c>Product</c>'s <c>ean</c> renamed.</summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhoseProductPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-catalog-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace("    Product:\n      type: object\n      properties:\n        code:\n          $ref: '#/components/schemas/ProductCode'\n        ean:",
                          "    Product:\n      type: object\n      properties:\n        code:\n          $ref: '#/components/schemas/ProductCode'\n        eanRenamed:", StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "Product").ToHashSet(StringComparer.Ordinal);
            var declaredByRecord = AsyncApiSchema.PropertyNamesOfRecord(typeof(ProductPayload));

            Assert.NotEqual(declaredByRecord, parsedFromScratch);
            Assert.DoesNotContain("ean", parsedFromScratch);
            Assert.Contains("eanRenamed", parsedFromScratch);
        }
        finally
        {
            File.Delete(scratchPath);
        }
    }

    private static JsonDocument RoundTrip<T>(T payload)
    {
        var bytes = RpcJson.Serialize(payload);
        var roundTripped = RpcJson.Deserialize<T>(bytes);
        var roundTrippedBytes = RpcJson.Serialize(roundTripped);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(bytes), System.Text.Encoding.UTF8.GetString(roundTrippedBytes));
        return JsonDocument.Parse(bytes);
    }

    private static void AssertKeys(JsonDocument document, params string[] expectedKeys)
    {
        var actualKeys = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedKeys.ToHashSet(StringComparer.Ordinal), actualKeys);
    }
}
