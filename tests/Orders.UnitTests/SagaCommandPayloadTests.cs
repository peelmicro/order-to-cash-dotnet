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
    /// `BI21` (design.md §10.4) — `SagaCommandRequestFactory.BuildInvoiceIssue`
    /// already passes <c>order.InitialDiscount.MinorUnits</c>; what was
    /// missing was the guard. Driven from ONE real <see cref="OrderToCash.Orders.Domain.Order"/>
    /// with a genuinely non-zero discount, building BOTH the `invoice.issue`
    /// and `credit.hold` payloads from that SAME order — the shared source
    /// is what makes the three-way comparison mean something.
    /// </summary>
    [Fact]
    public void BI21_CarriesTheOrdersInitialDiscountOnTheInvoiceIssueRequest_SoTheInvoiceTotalEqualsTheCreditHoldAmountAndTheOrderTotal()
    {
        var order = OrderTestData.PlacedOrder();
        Assert.NotEqual(0, order.InitialDiscount.MinorUnits); // the comparison below means nothing against a zero discount.

        var invoiceJson = OrderToCash.Orders.Application.Sagas.SagaCommandRequestFactory.BuildJson(OrderToCash.Orders.Application.Sagas.SagaCommandKind.InvoiceIssue, order);
        var creditHoldJson = OrderToCash.Orders.Application.Sagas.SagaCommandRequestFactory.BuildJson(OrderToCash.Orders.Application.Sagas.SagaCommandKind.CreditHold, order);

        var invoiceRequest = JsonSerializer.Deserialize<InvoiceIssueRequestPayload>(invoiceJson, JsonWire.Options)!;
        var creditHoldRequest = JsonSerializer.Deserialize<CreditHoldRequestPayload>(creditHoldJson, JsonWire.Options)!;

        var invoiceLineSum = invoiceRequest.Lines.Sum(l => l.UnitPrice * (long)l.Units);
        var invoiceTotal = invoiceLineSum - (invoiceRequest.Discount ?? 0);

        Assert.Equal(invoiceTotal, creditHoldRequest.Amount.Amount);
        Assert.Equal(order.TotalAmount.MinorUnits, creditHoldRequest.Amount.Amount);
        Assert.Equal(order.TotalAmount.MinorUnits, invoiceTotal);
    }

    /// <summary>
    /// Every payload record the `BC23` theory below covers — the completeness
    /// set `orders_wire_key_theory_compares_a_hand_typed_list_to_itself`'s own
    /// enumeration bullet asks for. Re-review (round 2) found this set had
    /// dropped <see cref="SagaMoney"/> (the record backing `asyncapi.yaml`'s
    /// <c>Money</c> schema) — see `progress/impl_guard_hardening.md`'s
    /// "Fix round — re-review's three items" section for the full
    /// <c>grep -n "record "</c> enumeration this row was reconstructed from.
    /// <c>StockReserveRequestLine</c>
    /// is the one declared record genuinely NOT in this set: `asyncapi.yaml`
    /// declares <c>StockReserveRequestPayload.lines[]</c>'s item shape INLINE,
    /// with no named schema, so <see cref="AsyncApiSchema.PropertyNamesOf(string)"/>
    /// has nothing to look up for it.
    /// </summary>
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "StockReserveRequestPayload", typeof(StockReserveRequestPayload) },
        { "StockReserveReplyPayload", typeof(StockReserveReplyPayload) },
        { "StockReleaseRequestPayload", typeof(StockReleaseRequestPayload) },
        { "StockReleaseReplyPayload", typeof(StockReleaseReplyPayload) },
        { "DespatchCreateRequestPayload", typeof(DespatchCreateRequestPayload) },
        { "DespatchCreateReplyPayload", typeof(DespatchCreateReplyPayload) },
        { "Money", typeof(SagaMoney) },
        { "CreditHoldRequestPayload", typeof(CreditHoldRequestPayload) },
        { "CreditHoldReplyPayload", typeof(CreditHoldReplyPayload) },
        { "InvoiceIssueRequestPayload", typeof(InvoiceIssueRequestPayload) },
        { "InvoiceIssueReplyPayload", typeof(InvoiceIssueReplyPayload) },
        { "CreditReleaseRequestPayload", typeof(CreditReleaseRequestPayload) },
        { "CreditReleaseReplyPayload", typeof(CreditReleaseReplyPayload) },
    };

    /// <summary>
    /// Backlog id 51, `BC23` (design.md §10.2) — originally compared a
    /// SCHEMA-PARSED key set against a hand-retyped key list living in this
    /// same file's own <c>[InlineData]</c>, never against the payload record
    /// that actually implements the contract. That guard could not fail on
    /// the exact defect it was added for: removing
    /// <c>availableCreditAfter</c> from <see cref="CreditReleaseReplyPayload"/>
    /// left <c>Orders.UnitTests</c> 342/342 green, because both sides being
    /// compared were restatements of the SAME assumption, never a reading of
    /// the record. Backlog id 64 ports Billing's own
    /// <c>CreditRpcPayloadTests.cs:29-35</c> form — <c>expected</c> parsed
    /// from the spec, <c>actual</c> read by reflection off the payload TYPE
    /// itself — which is where the guard's teeth actually are.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(string schemaName, Type payloadType)
    {
        var expected = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var actual = payloadType.GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with <c>CreditHoldReplyPayload</c>'s
    /// <c>orderReference</c> renamed: parsing the scratch copy no longer
    /// agrees with <see cref="CreditHoldReplyPayload"/>'s actual property
    /// set, read by REFLECTION off the record — never a hand-retyped list —
    /// exactly what would make the real
    /// <see cref="BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped"/>
    /// case fail if the spec really changed this way.
    /// </summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhoseCreditHoldReplyPayloadPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-orders-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace("        orderReference:\n          $ref: '#/components/schemas/OrderReference'\n        creditCode:\n          $ref: '#/components/schemas/CreditCode'\n        currency:\n          $ref: '#/components/schemas/CurrencyCode'\n        heldAmount:",
                          "        orderReferenceRenamed:\n          $ref: '#/components/schemas/OrderReference'\n        creditCode:\n          $ref: '#/components/schemas/CreditCode'\n        currency:\n          $ref: '#/components/schemas/CurrencyCode'\n        heldAmount:", StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "CreditHoldReplyPayload").ToHashSet(StringComparer.Ordinal);
            var actual = typeof(CreditHoldReplyPayload).GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

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
