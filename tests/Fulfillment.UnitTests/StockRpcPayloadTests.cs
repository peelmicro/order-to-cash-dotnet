using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The <c>SagaCommandPayloadTests</c> instrument, in two halves.
///
/// <para>The <c>[Fact]</c> cases below serialise a real instance and assert
/// the keys that actually REACH the wire for that instance — which optional
/// was omitted, which was present — through the ONE shared
/// <see cref="JsonWire.Options"/> (camelCase, nulls omitted).</para>
///
/// <para>The two <c>BC23</c> theories
/// (<see cref="BC23_EveryStockAndDespatchRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped"/>
/// and <see cref="BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims"/>)
/// assert the CONTRACT: the record's own property set, read by reflection,
/// against the set parsed from <c>specs/shared/asyncapi.yaml</c>. Backlog
/// id 70 replaced this file's previous hand-retyped key lists with that
/// reading — a literal key list can only restate an assumption, and cannot
/// notice a property ADDED to the record, because a null optional never
/// reaches the wire for the <c>[Fact]</c> cases to see.</para>
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

    /// <summary>
    /// The rows this file's two <c>BC23</c> theories below walk: an
    /// <c>asyncapi.yaml</c> schema name paired with the RECORD that claims
    /// it. Every one of these twelve records lives in
    /// <c>OrderToCash.Contracts.Rpc</c> — feature 76 moved them there and
    /// unified Fulfillment's responder-side copy with Orders' caller-side
    /// copy, so there is exactly ONE definition of each and this table
    /// names it unambiguously (backlog id 70, bullet 2).
    /// </summary>
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "StockCheckRequestPayload", typeof(StockCheckRequestPayload) },
        { "StockCheckReplyPayload", typeof(StockCheckReplyPayload) },
        { "StockReserveRequestPayload", typeof(StockReserveRequestPayload) },
        { "StockReserveReplyPayload", typeof(StockReserveReplyPayload) },
        { "StockReleaseRequestPayload", typeof(StockReleaseRequestPayload) },
        { "StockReleaseReplyPayload", typeof(StockReleaseReplyPayload) },
        { "StockListRequestPayload", typeof(StockListRequestPayload) },
        { "StockListReplyPayload", typeof(StockListReplyPayload) },
        { "StockReplenishRequestPayload", typeof(StockReplenishRequestPayload) },
        { "StockReplenishReplyPayload", typeof(StockReplenishReplyPayload) },
        { "DespatchCreateRequestPayload", typeof(DespatchCreateRequestPayload) },
        { "DespatchCreateReplyPayload", typeof(DespatchCreateReplyPayload) },
    };

    /// <summary>
    /// Backlog id 51, `BC23` — and backlog id 70, which retired this
    /// theory's previous HAND-RETYPED key list. That list compared a
    /// literal array against the set parsed from
    /// <c>specs/shared/asyncapi.yaml</c> and never read the payload record,
    /// so an UNDECLARED property added to
    /// <see cref="DespatchCreateReplyPayload"/> left the whole of
    /// <c>Fulfillment.UnitTests</c> green: nulls are OMITTED by
    /// <see cref="JsonWire.Options"/>, so the serialised-key cases above
    /// could not see it either. Those cases are kept — they catch a
    /// different defect (which keys actually reach the wire for a given
    /// reply outcome).
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryStockAndDespatchRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties(schemaName, payloadType);
    }

    /// <summary>
    /// Backlog id 70, bullet 3 — the schema NAME each row claims is
    /// guarded too, not only the key set. This file is the reason that
    /// bullet exists: <c>StockCheckRequestPayload</c> and
    /// <c>StockReplenishRequestPayload</c> both declare exactly
    /// <c>{ companyCode, lines }</c>, so transposing those two row labels
    /// is COMPLETELY invisible to the key-set case above.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertTheRowsSchemaNameNamesTheRecordItClaims(schemaName, payloadType);
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with <c>StockCheckRequestPayload</c>'s
    /// <c>companyCode</c> renamed: the RECORD's own property set no longer
    /// agrees with the scratch copy, and the rename is asserted in both
    /// directions so the case cannot pass on any old inequality. Backlog
    /// id 70 replaced the hand-retyped literal this used to compare against
    /// — a literal cannot disagree with a spec for the reason the guard
    /// exists to detect.
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
            var declaredByRecord = AsyncApiSchema.PropertyNamesOfRecord(typeof(StockCheckRequestPayload));

            Assert.NotEqual(declaredByRecord, parsedFromScratch);
            Assert.DoesNotContain("companyCode", parsedFromScratch);
            Assert.Contains("companyCodeRenamed", parsedFromScratch);
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
