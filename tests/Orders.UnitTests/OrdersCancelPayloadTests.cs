using System.Text.Json;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The <c>StockRpcPayloadTests</c>/<c>CatalogReferenceListPayloadTests</c>
/// instrument, applied to <c>orders.cancel</c>'s own payload records: the
/// <c>[Fact]</c> cases serialise a real instance and assert the keys that
/// actually REACH the wire through the ONE shared
/// <see cref="JsonWire.Options"/> (camelCase, nulls omitted), and the two
/// <c>BC23</c> theories assert the CONTRACT — the record's own property
/// set, read by reflection, against the set parsed from
/// <c>specs/shared/asyncapi.yaml</c> (backlog id 70, which replaced this
/// file's hand-retyped key lists with that reading).
/// </summary>
public sealed class OrdersCancelPayloadTests
{
    [Fact]
    public void OrdersCancelRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new OrdersCancelRequestPayload(Guid.NewGuid(), "ORD-000001", "operator_cancelled", "please cancel");
        var json = RoundTrip(payload);

        AssertKeys(json, "orderId", "orderReference", "reason", "note");
    }

    [Fact]
    public void OrdersCancelRequestPayload_OmitsAbsentOptionalsRatherThanEmittingNull()
    {
        var payload = new OrdersCancelRequestPayload(Guid.NewGuid(), OrderReference: null, "operator_cancelled", Note: null);
        var json = RoundTrip(payload);

        AssertKeys(json, "orderId", "reason");
    }

    /// <summary>The immediate-cancel branch: <c>cancellationReason</c> present, <c>compensationPlanned</c> present but empty — the schema's own REQUIRED field, never omitted even when nothing is planned.</summary>
    [Fact]
    public void OrdersCancelReplyPayload_ImmediateCancel_CarriesCancellationReasonAndAnEmptyCompensationPlannedArray()
    {
        var payload = new OrdersCancelReplyPayload(Guid.NewGuid(), "ORD-000001", "cancelled", CompensationPlanned: [], CancellationReason: "operator_cancelled");
        var json = RoundTrip(payload);

        AssertKeys(json, "orderId", "orderReference", "status", "compensationPlanned", "cancellationReason");
        Assert.Equal(0, json.RootElement.GetProperty("compensationPlanned").GetArrayLength());
    }

    /// <summary>The compensation-pending branches: <c>cancellationReason</c> OMITTED (not null) — the order has not reached <c>cancelled</c> yet — while <c>compensationPlanned</c> names what will be released, in release order (SA-4: the contested resource, stock, first at <c>credit_approved</c>/<c>confirmed</c> — saga.md §4.3).</summary>
    [Fact]
    public void OrdersCancelReplyPayload_CompensationPending_OmitsCancellationReasonAndNamesThePlannedReleasesInOrder()
    {
        var payload = new OrdersCancelReplyPayload(Guid.NewGuid(), "ORD-000001", "confirmed", CompensationPlanned: ["stock_release", "credit_release"]);
        var json = RoundTrip(payload);

        AssertKeys(json, "orderId", "orderReference", "status", "compensationPlanned");
        Assert.False(json.RootElement.TryGetProperty("cancellationReason", out _), "an absent CancellationReason must be omitted, not written as null");
        var planned = json.RootElement.GetProperty("compensationPlanned");
        Assert.Equal("stock_release", planned[0].GetString());
        Assert.Equal("credit_release", planned[1].GetString());
    }

    /// <summary>
    /// The rows this file's two <c>BC23</c> theories below walk: an
    /// <c>asyncapi.yaml</c> schema name paired with the RECORD that claims
    /// it. The record is <c>OrderToCash.Orders.Presentation.Rpc</c>'s —
    /// Orders is the service that ANSWERS <c>orders.cancel</c>, so its
    /// Presentation copy is the one that shapes the wire. The Gateway
    /// declares its own caller-side <c>OrdersCancelReplyPayload</c>
    /// (<c>src/Gateway/Application/Rpc/GatewayRpcPayloads.cs</c>, kept
    /// because <c>src/Contracts/Rpc</c> has no counterpart for this
    /// subject — backlog id 84), and that second definition has its own
    /// row in <c>tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs</c>.
    /// </summary>
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "OrdersCancelRequestPayload", typeof(OrdersCancelRequestPayload) },
        { "OrdersCancelReplyPayload", typeof(OrdersCancelReplyPayload) },
    };

    /// <summary>
    /// Backlog id 51, `BC23`'s discipline — and backlog id 70, which
    /// retired this theory's previous HAND-RETYPED key list. It compared
    /// two pieces of text (a literal array here against the sets parsed
    /// from <c>specs/shared/asyncapi.yaml</c>) and never read the payload
    /// record at all, so an UNDECLARED property added to
    /// <see cref="OrdersCancelReplyPayload"/> left this file — and the
    /// whole of <c>Orders.UnitTests</c> — green: nulls are OMITTED by
    /// <see cref="JsonWire.Options"/>, so the serialised-key cases above
    /// could not see it either. The key set is now read off the record by
    /// reflection, the shape
    /// <c>tests/Billing.UnitTests/CreditRpcPayloadTests.cs</c> established.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryOrdersCancelRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties(schemaName, payloadType);
    }

    /// <summary>
    /// Backlog id 70, bullet 3 — the schema NAME each row claims is
    /// guarded too, not only the key set: substituting a real sibling
    /// schema name fails here, naming both halves of the row, including
    /// where two sibling schemas declare identical keys and the key-set
    /// case above therefore cannot see the swap.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims(string schemaName, Type payloadType)
    {
        AsyncApiSchema.AssertTheRowsSchemaNameNamesTheRecordItClaims(schemaName, payloadType);
    }

    /// <summary>`G5` — the arming that proves the guard has teeth: a SCRATCH copy of the real spec (never the real, read-only <c>specs/shared/asyncapi.yaml</c>) with <c>OrdersCancelRequestPayload</c>'s <c>orderReference</c> renamed.</summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhoseOrdersCancelRequestPayloadPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-orders-cancel-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace(
                    "    OrdersCancelRequestPayload:\n      type: object\n      properties:\n        orderId:\n          $ref: '#/components/schemas/UniqueId'\n        orderReference:",
                    "    OrdersCancelRequestPayload:\n      type: object\n      properties:\n        orderId:\n          $ref: '#/components/schemas/UniqueId'\n        orderReferenceRenamed:",
                    StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "OrdersCancelRequestPayload").ToHashSet(StringComparer.Ordinal);
            var declaredByRecord = AsyncApiSchema.PropertyNamesOfRecord(typeof(OrdersCancelRequestPayload));

            Assert.NotEqual(declaredByRecord, parsedFromScratch);
            Assert.DoesNotContain("orderReference", parsedFromScratch);
            Assert.Contains("orderReferenceRenamed", parsedFromScratch);
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
