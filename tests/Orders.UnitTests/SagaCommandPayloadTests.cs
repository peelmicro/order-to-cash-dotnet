using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §6.1 — every saga command payload round-trips through the ONE
/// shared <see cref="JsonWire.Options"/> (camelCase, nulls omitted), and an
/// absent optional field is OMITTED from the wire, never emitted as
/// <c>null</c>.
/// </summary>
public sealed class SagaCommandPayloadTests
{
    [Fact]
    public void StockReserveRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReserveRequestPayload(
            "ORD-000001",
            "RETAILER1",
            "COMPANY1",
            [new StockReserveRequestLine("SKU-1", 3)]);

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "retailerCode", "companyCode", "lines");
        var line = json.RootElement.GetProperty("lines")[0];
        AssertKeys(line, "productCode", "units");
    }

    [Fact]
    public void StockReserveReplyPayload_OmitsAbsentOptionalsRatherThanEmittingNull()
    {
        var payload = new StockReserveReplyPayload("accepted", "ORD-000001", [new ReservationRef(Guid.NewGuid(), "SKU-1", 3)]);

        var json = RoundTrip(payload);

        AssertKeys(json, "outcome", "orderReference", "reservations");
        Assert.False(json.RootElement.TryGetProperty("shortages", out _), "an absent Shortages must be omitted, not written as null");

        var rejected = new StockReserveReplyPayload("rejected", "ORD-000001", Shortages: [new Shortage("SKU-1", 3, 1)]);
        var rejectedJson = RoundTrip(rejected);
        Assert.False(rejectedJson.RootElement.TryGetProperty("reservations", out _));
        AssertKeys(rejectedJson, "outcome", "orderReference", "shortages");
    }

    [Fact]
    public void StockReleaseRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new StockReleaseRequestPayload("ORD-000001", "credit_rejected");

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
    public void DespatchCreateRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new DespatchCreateRequestPayload("ORD-000001");

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference");
    }

    [Fact]
    public void DespatchCreateReplyPayload_OmitsAbsentLinesRatherThanEmittingNull()
    {
        var payload = new DespatchCreateReplyPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, Created: true);

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "despatchReference", "despatchDate", "created");
        Assert.False(json.RootElement.TryGetProperty("lines", out _));
    }

    [Fact]
    public void CreditHoldRequestPayload_CarriesANestedMoneyObjectWithAmountAndCurrency()
    {
        var payload = new CreditHoldRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", new SagaMoney(124_250, "EUR"));

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "retailerCode", "companyCode", "amount");
        AssertKeys(json.RootElement.GetProperty("amount"), "amount", "currency");
        Assert.Equal(124_250, json.RootElement.GetProperty("amount").GetProperty("amount").GetInt64());
        Assert.Equal("EUR", json.RootElement.GetProperty("amount").GetProperty("currency").GetString());
    }

    [Fact]
    public void CreditHoldReplyPayload_OmitsAbsentOptionalsRatherThanEmittingNull()
    {
        var approved = new CreditHoldReplyPayload("approved", "ORD-000001", "EUR", 500_00, CreditCode: "CR-000001", HeldAmount: 124_250);
        var approvedJson = RoundTrip(approved);
        AssertKeys(approvedJson, "outcome", "orderReference", "currency", "availableCredit", "creditCode", "heldAmount");
        Assert.False(approvedJson.RootElement.TryGetProperty("reason", out _));

        var rejected = new CreditHoldReplyPayload("rejected", "ORD-000001", "EUR", 500_00, Reason: "over_limit");
        var rejectedJson = RoundTrip(rejected);
        AssertKeys(rejectedJson, "outcome", "orderReference", "currency", "availableCredit", "reason");
        Assert.False(rejectedJson.RootElement.TryGetProperty("creditCode", out _));
        Assert.False(rejectedJson.RootElement.TryGetProperty("heldAmount", out _));
    }

    [Fact]
    public void InvoiceIssueRequestPayload_SerialisesWithTheDeclaredCamelCaseKeys()
    {
        var payload = new InvoiceIssueRequestPayload(
            "ORD-000001",
            "RETAILER1",
            "COMPANY1",
            "EUR",
            [new InvoiceLine("SKU-1", 3, 4_000)],
            Discount: 0);

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "retailerCode", "companyCode", "currency", "lines", "discount");
        AssertKeys(json.RootElement.GetProperty("lines")[0], "productCode", "units", "unitPrice");
    }

    [Fact]
    public void InvoiceIssueReplyPayload_OmitsAbsentInvoiceIdRatherThanEmittingNull()
    {
        var payload = new InvoiceIssueReplyPayload("ORD-000001", "INV-000001", DateTimeOffset.UtcNow, "EUR", 124_250, "issued", Created: true);

        var json = RoundTrip(payload);

        AssertKeys(json, "orderReference", "invoiceReference", "invoiceDate", "currency", "totalAmount", "status", "created");
        Assert.False(json.RootElement.TryGetProperty("invoiceId", out _));
    }

    /// <summary>
    /// Round-trips through <see cref="RpcJson"/> and asserts the round-tripped
    /// value re-serialises to the SAME bytes — a deep-equality check that
    /// tolerates <c>record</c> equality's own blind spot for
    /// <see cref="IReadOnlyList{T}"/> members (default equality compares
    /// list REFERENCES, not sequence contents, so two structurally-identical
    /// lists built by different code paths would otherwise report unequal).
    /// </summary>
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
