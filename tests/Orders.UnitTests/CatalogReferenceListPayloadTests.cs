using System.Text.Json;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The <c>StockRpcPayloadTests</c>/<c>CreditRpcPayloadTests</c> instrument,
/// applied to <c>catalog.reference.list</c>'s own payload records: every one
/// round-trips through the ONE shared <see cref="JsonWire.Options"/>
/// (camelCase, nulls omitted) with exactly the keys this file hand-retypes,
/// and <see cref="BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi"/>
/// pins those hand-retyped lists against <c>specs/shared/asyncapi.yaml</c>
/// itself, read as text.
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

    /// <summary>Backlog id 51, `BC23`'s discipline — this file's hand-retyped key lists agree with the sets parsed from <c>specs/shared/asyncapi.yaml</c> via <see cref="AsyncApiSchema"/>.</summary>
    [Theory]
    [InlineData("CatalogReferenceListRequestPayload", new[] { "kinds", "includeDisabled" })]
    [InlineData("Product", new[] { "code", "ean", "name", "description", "price", "currency", "enabled" })]
    [InlineData("Party", new[] { "code", "name", "country", "vat", "gln", "currency", "enabled" })]
    [InlineData("CurrencyView", new[] { "code", "isoNumber", "symbol", "decimalPoints" })]
    [InlineData("CatalogReferenceListReplyPayload", new[] { "products", "retailers", "companies", "currencies" })]
    public void BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi(string schemaName, string[] handRetypedKeys)
    {
        var parsed = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var retyped = handRetypedKeys.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(parsed, retyped);
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
            var handRetyped = new[] { "code", "ean", "name", "description", "price", "currency", "enabled" }.ToHashSet(StringComparer.Ordinal);

            Assert.NotEqual(handRetyped, parsedFromScratch);
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
