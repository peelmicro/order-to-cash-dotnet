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
    /// Backlog id 51, `BC23` (design.md §10.2) — this file's own hand-retyped
    /// key lists (every <c>AssertKeys</c> call above) are cheap and readable
    /// and catch UNILATERAL drift of the code; what they cannot catch is the
    /// CORRELATED authoring error where the schema and this file's retyped
    /// copy are changed together, wrongly, and stay green. This one case
    /// closes exactly that gap, parsing the same key sets from
    /// <c>specs/shared/asyncapi.yaml</c> via <see cref="AsyncApiSchema"/>
    /// rather than retyping a third copy.
    /// </summary>
    [Theory]
    [InlineData("StockReserveRequestPayload", new[] { "orderReference", "retailerCode", "companyCode", "lines" })]
    [InlineData("StockReserveReplyPayload", new[] { "outcome", "orderReference", "reservations", "shortages" })]
    [InlineData("StockReleaseRequestPayload", new[] { "orderReference", "reason" })]
    [InlineData("StockReleaseReplyPayload", new[] { "outcome", "orderReference", "released" })]
    [InlineData("DespatchCreateRequestPayload", new[] { "orderReference" })]
    [InlineData("DespatchCreateReplyPayload", new[] { "orderReference", "despatchReference", "despatchDate", "created", "lines" })]
    [InlineData("CreditHoldRequestPayload", new[] { "orderReference", "retailerCode", "companyCode", "amount" })]
    [InlineData("CreditHoldReplyPayload", new[] { "outcome", "orderReference", "creditCode", "currency", "heldAmount", "availableCredit", "reason" })]
    [InlineData("InvoiceIssueRequestPayload", new[] { "orderReference", "retailerCode", "companyCode", "currency", "lines", "discount" })]
    [InlineData("InvoiceIssueReplyPayload", new[] { "orderReference", "invoiceId", "invoiceReference", "invoiceDate", "currency", "totalAmount", "status", "created" })]
    // Review round 2, D2: this feature added CreditReleaseRequestPayload/CreditReleaseReplyPayload (billing.credit.release,
    // the sixth saga command) and skipped this file's own BC23 guard for them — the omission is why the reply payload's
    // required availableCreditAfter shipped missing.
    [InlineData("CreditReleaseRequestPayload", new[] { "orderReference", "retailerCode", "companyCode" })]
    [InlineData("CreditReleaseReplyPayload", new[] { "released", "orderReference", "creditCode", "currency", "releasedAmount", "availableCreditAfter" })]
    public void BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi(string schemaName, string[] handRetypedKeys)
    {
        var parsed = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var retyped = handRetypedKeys.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(parsed, retyped);
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with <c>CreditHoldReplyPayload</c>'s
    /// <c>orderReference</c> renamed: the hand-retyped list this file uses
    /// for that schema no longer agrees with the scratch copy.
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
            var handRetyped = new[] { "outcome", "orderReference", "creditCode", "currency", "heldAmount", "availableCredit", "reason" }.ToHashSet(StringComparer.Ordinal);

            Assert.NotEqual(handRetyped, parsedFromScratch);
        }
        finally
        {
            File.Delete(scratchPath);
        }
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
