using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The <c>SagaCommandPayloadTests</c> instrument: every one of the ten
/// <c>fulfillment.stock.*</c> payload records round-trips through the ONE
/// shared <see cref="JsonWire.Options"/> (camelCase, nulls omitted) with
/// exactly the keys this file hand-retypes below — a cheap, readable check
/// that catches UNILATERAL drift of the code. <see cref="BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi"/>
/// is the ONE case that actually reads <c>specs/shared/asyncapi.yaml</c> as
/// text (backlog id 51, `BC23`, design.md §10.2) and closes the gap this
/// file's hand-retyping alone cannot: a CORRELATED authoring error where
/// the schema and this file's copy are changed together, wrongly, and stay
/// green.
/// </summary>
public sealed class StockRpcPayloadTests
{
    [Fact]
    public void StockCheckRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 3)]);
        var json = RoundTrip(payload);

        AssertKeys(json, "companyCode", "lines");
        AssertKeys(json.RootElement.GetProperty("lines")[0], "productCode", "quantity");
    }

    [Fact]
    public void StockCheckReplyPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockCheckReplyPayload(true, [new StockCheckReplyLine("P1", 3, 10, true)]);
        var json = RoundTrip(payload);

        AssertKeys(json, "available", "lines");
        AssertKeys(json.RootElement.GetProperty("lines")[0], "productCode", "requested", "available", "sufficient");
    }

    [Fact]
    public void StockReserveRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 3)]);
        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "retailerCode", "companyCode", "lines");
        AssertKeys(json.RootElement.GetProperty("lines")[0], "productCode", "units");
    }

    [Fact]
    public void StockReserveReplyPayload_OmitsAbsentOptionalsRatherThanEmittingNull()
    {
        var accepted = new StockReserveReplyPayload("accepted", "ORD-000001", [new ReservationRef(Guid.NewGuid(), "P1", 3)]);
        var acceptedJson = RoundTrip(accepted);
        AssertKeys(acceptedJson, "outcome", "orderReference", "reservations");
        Assert.False(acceptedJson.RootElement.TryGetProperty("shortages", out _));

        var rejected = new StockReserveReplyPayload("rejected", "ORD-000001", Shortages: [new Shortage("P1", 3, 1)]);
        var rejectedJson = RoundTrip(rejected);
        AssertKeys(rejectedJson, "outcome", "orderReference", "shortages");
        Assert.False(rejectedJson.RootElement.TryGetProperty("reservations", out _));
    }

    [Fact]
    public void StockReleaseRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReleaseRequestPayload("ORD-000001", "order_cancelled");
        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "reason");
    }

    [Fact]
    public void StockReleaseReplyPayload_OmitsAbsentReleasedRatherThanEmittingNull()
    {
        var payload = new StockReleaseReplyPayload("already_released", "ORD-000001");
        var json = RoundTrip(payload);

        AssertKeys(json, "outcome", "orderReference");
        Assert.False(json.RootElement.TryGetProperty("released", out _));
    }

    [Fact]
    public void StockListRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockListRequestPayload(2, 50, "ACME", "P1", true);
        var json = RoundTrip(payload);

        AssertKeys(json, "page", "pageSize", "companyCode", "productCode", "belowThreshold");
    }

    [Fact]
    public void StockListReplyPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockListReplyPayload([new StockViewPayload("ACME", "P1", 10, 3, 7, 5)], new StockPageInfo(1, 25, 1));
        var json = RoundTrip(payload);

        AssertKeys(json, "items", "page");
        AssertKeys(json.RootElement.GetProperty("items")[0], "companyCode", "productCode", "units", "reservedUnits", "availableUnits", "lowStockThreshold");
        AssertKeys(json.RootElement.GetProperty("page"), "page", "pageSize", "total");
    }

    [Fact]
    public void StockReplenishRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("P1", 20)]);
        var json = RoundTrip(payload);

        AssertKeys(json, "companyCode", "lines");
        AssertKeys(json.RootElement.GetProperty("lines")[0], "productCode", "units");
    }

    [Fact]
    public void StockReplenishReplyPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReplenishReplyPayload([new StockViewPayload("ACME", "P1", 30, 3, 27, 5)]);
        var json = RoundTrip(payload);

        AssertKeys(json, "items");
    }

    [Fact]
    public void DespatchCreateRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new DespatchCreateRequestPayload("ORD-000001");
        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference");
    }

    [Fact]
    public void DespatchCreateReplyPayload_OmitsAbsentLinesRatherThanEmittingNull()
    {
        var withoutLines = new DespatchCreateReplyPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, false);
        var withoutLinesJson = RoundTrip(withoutLines);
        AssertKeys(withoutLinesJson, "orderReference", "despatchReference", "despatchDate", "created");
        Assert.False(withoutLinesJson.RootElement.TryGetProperty("lines", out _));

        var withLines = new DespatchCreateReplyPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, true, [new DespatchLine("P1", 3)]);
        var withLinesJson = RoundTrip(withLines);
        AssertKeys(withLinesJson, "orderReference", "despatchReference", "despatchDate", "created", "lines");
        AssertKeys(withLinesJson.RootElement.GetProperty("lines")[0], "productCode", "units");
    }

    /// <summary>Backlog id 51, `BC23` — this file's hand-retyped key lists agree with the sets parsed from <c>specs/shared/asyncapi.yaml</c> via <see cref="AsyncApiSchema"/>, schema by schema. Existing cases above are kept; they catch a different defect.</summary>
    [Theory]
    [InlineData("StockCheckRequestPayload", new[] { "companyCode", "lines" })]
    [InlineData("StockCheckReplyPayload", new[] { "available", "lines" })]
    [InlineData("StockReserveRequestPayload", new[] { "orderReference", "retailerCode", "companyCode", "lines" })]
    [InlineData("StockReserveReplyPayload", new[] { "outcome", "orderReference", "reservations", "shortages" })]
    [InlineData("StockReleaseRequestPayload", new[] { "orderReference", "reason" })]
    [InlineData("StockReleaseReplyPayload", new[] { "outcome", "orderReference", "released" })]
    [InlineData("StockListRequestPayload", new[] { "page", "pageSize", "companyCode", "productCode", "belowThreshold" })]
    [InlineData("StockListReplyPayload", new[] { "items", "page" })]
    [InlineData("StockReplenishRequestPayload", new[] { "companyCode", "lines" })]
    [InlineData("StockReplenishReplyPayload", new[] { "items" })]
    [InlineData("DespatchCreateRequestPayload", new[] { "orderReference" })]
    [InlineData("DespatchCreateReplyPayload", new[] { "orderReference", "despatchReference", "despatchDate", "created", "lines" })]
    public void BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi(string schemaName, string[] handRetypedKeys)
    {
        var parsed = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var retyped = handRetypedKeys.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(parsed, retyped);
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with <c>StockCheckRequestPayload</c>'s
    /// <c>companyCode</c> renamed: this file's hand-retyped list for that
    /// schema no longer agrees with the scratch copy.
    /// </summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhoseStockCheckRequestPayloadPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-fulfillment-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace("    StockCheckRequestPayload:\n      type: object\n      properties:\n        companyCode:",
                          "    StockCheckRequestPayload:\n      type: object\n      properties:\n        companyCodeRenamed:", StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "StockCheckRequestPayload").ToHashSet(StringComparer.Ordinal);
            var handRetyped = new[] { "companyCode", "lines" }.ToHashSet(StringComparer.Ordinal);

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

    private static void AssertKeys(JsonDocument document, params string[] expectedKeys) => AssertKeys(document.RootElement, expectedKeys);

    private static void AssertKeys(JsonElement element, params string[] expectedKeys)
    {
        var actualKeys = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedKeys.ToHashSet(StringComparer.Ordinal), actualKeys);
    }
}
