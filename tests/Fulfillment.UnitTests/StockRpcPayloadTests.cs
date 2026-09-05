using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The <c>SagaCommandPayloadTests</c> instrument (design.md §6.3): every one
/// of the ten <c>fulfillment.stock.*</c> payload records round-trips through
/// the ONE shared <see cref="JsonWire.Options"/> (camelCase, nulls omitted)
/// with exactly the keys <c>specs/shared/asyncapi.yaml</c> declares.
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
