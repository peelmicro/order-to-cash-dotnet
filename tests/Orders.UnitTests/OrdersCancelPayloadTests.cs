using System.Text.Json;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The <c>StockRpcPayloadTests</c>/<c>CatalogReferenceListPayloadTests</c>
/// instrument, applied to <c>orders.cancel</c>'s own payload records: every
/// one round-trips through the ONE shared <see cref="JsonWire.Options"/>
/// (camelCase, nulls omitted) with exactly the keys this file hand-retypes,
/// pinned against <c>specs/shared/asyncapi.yaml</c> itself (BC23's
/// discipline).
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

    /// <summary>Backlog id 51, `BC23`'s discipline — this file's hand-retyped key lists agree with the sets parsed from <c>specs/shared/asyncapi.yaml</c> via <see cref="AsyncApiSchema"/>.</summary>
    [Theory]
    [InlineData("OrdersCancelRequestPayload", new[] { "orderId", "orderReference", "reason", "note" })]
    [InlineData("OrdersCancelReplyPayload", new[] { "orderId", "orderReference", "status", "cancellationReason", "compensationPlanned" })]
    public void BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi(string schemaName, string[] handRetypedKeys)
    {
        var parsed = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var retyped = handRetypedKeys.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(parsed, retyped);
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
            var handRetyped = new[] { "orderId", "orderReference", "reason", "note" }.ToHashSet(StringComparer.Ordinal);

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
