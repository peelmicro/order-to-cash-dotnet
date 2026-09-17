using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Feature <c>api_tests</c> (id 31, phase 18) — black-box API tests through
/// the REAL Gateway (<see cref="GatewayTestHost"/>: real Kestrel, an
/// ephemeral TCP port, an <see cref="HttpClient"/> that only ever talks
/// HTTP — never <c>TestServer</c>) against a REAL fleet
/// (Orders/Fulfillment/Billing/Projector, Testcontainers MS-SQL x3 + Kafka +
/// NATS + MongoDB). Ported from #7's
/// <c>apps/gateway/src/black-box-api.integration.spec.ts</c> (648 lines) —
/// the assertion-by-assertion inventory, the ported-idiom ledger and the
/// arming table all live in <c>progress/impl_api_tests.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>#8 idiom, not #7's, for the fleet itself.</b> #7 spawned six real OS
/// processes because NestJS's DI graph could only be composed from a
/// process boundary. #8 has no such limitation — <see cref="SagaFleet"/>
/// (defined in <see cref="SagaEndToEndVerificationTests"/>, <c>internal</c>
/// and therefore visible to every class in this assembly) already boots
/// five real, unmodified service hosts IN-PROCESS. This class builds its
/// OWN <see cref="SagaFleet"/> instance — never the SAME live instance
/// <see cref="SagaEndToEndVerificationTests"/> holds, so no order this file
/// places can collide with one of that class's own criteria.
/// </para>
/// <para>
/// <b>Its own collection, its own four containers — deliberately NOT
/// <see cref="SagaE2ECollection"/>.</b> Tried first: joining
/// <see cref="SagaE2ECollection"/> to reuse its four container fixtures.
/// That FAILED live: <c>AssemblyBehavior.cs</c>'s
/// <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c>
/// serialises every COLLECTION in this assembly, but does nothing about two
/// classes inside the SAME collection each holding their own live,
/// statically-built <see cref="SagaFleet"/> (ten real service hosts plus
/// six MS-SQL databases) SIMULTANEOUSLY, once both classes have run at
/// least one <c>[Fact]</c> — neither fleet is torn down until every
/// <c>[Fact]</c> in the collection finishes. Measured: a full
/// <c>dotnet test</c> run of this project with that sharing in place
/// produced 5 spurious timeouts, ALL in
/// <see cref="SagaEndToEndVerificationTests"/> (never in this class),
/// 0 of them reproducing in isolation. A separate, named collection with
/// its own <c>DisableParallelization = true</c> — the SAME pattern
/// <c>NatsCollection</c>/<c>StreamProjectorEndToEndCollection</c> already
/// use in this project, per <c>AssemblyBehavior.cs</c>'s own header — makes
/// xUnit finish and DISPOSE <see cref="SagaE2ECollection"/> entirely before
/// this one's fixtures even start, so the two fleets are never live at the
/// same time. Re-measured after the change: 77/77, 0 failures, twice.
/// </para>
/// <para>
/// <b>No <c>mysql-worker-client.ts</c> workaround needed.</b> #7 read
/// downstream databases through a dedicated worker-process client because
/// <c>apps/gateway</c> itself was forbidden from resolving
/// <c>mysql2</c>/<c>drizzle-orm</c> (<c>no-write-database-client.spec.ts</c>).
/// #8 carries no such guard on a TEST project (CLAUDE.md's domain-purity
/// rule reaches only <c>Domain/</c> namespaces under <c>src/</c>), so this
/// file reads Billing's and Fulfillment's own write-model databases
/// directly via EF Core — the same idiom
/// <see cref="SagaEndToEndVerificationTests"/> already uses.
/// </para>
/// <para>
/// <b>Per-line <c>unitPrice</c> override, not a fixed .99-priced catalog
/// product.</b> #7 seeded <c>PRD-0001</c> at 24999 specifically so quantity
/// 1 would total <c>.99</c> (R42's trigger). #8's
/// <c>PlaceOrderRequestLineDto.UnitPrice</c> already lets a caller override
/// the catalog price per line — <see cref="SagaEndToEndVerificationTests"/>'s
/// own Criterion2 already uses this (<c>unitPrice: 1099</c>) — so no
/// dedicated fixture product is needed here either; this file reuses the
/// SAME technique.
/// </para>
/// </remarks>
[Collection(BlackBoxApiCollection.Name)]
public sealed class BlackBoxApiTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql, NatsContainerFixture nats, MongoContainerFixture mongo) : IAsyncLifetime
{
    private static readonly SemaphoreSlim _fleetGate = new(1, 1);
    private static SagaFleet? _fleet;

    public async Task InitializeAsync()
    {
        await _fleetGate.WaitAsync();
        try
        {
            _fleet ??= await SagaFleet.BuildAsync(kafka, mssql, nats, mongo);
        }
        finally
        {
            _fleetGate.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Called by <see cref="BlackBoxApiFleetTeardown"/> — never by an individual test.</summary>
    internal static async Task TearDownFleetIfBuiltAsync()
    {
        if (_fleet is { } fleet)
        {
            await fleet.DisposeAsync();
            _fleet = null;
        }
    }

    private SagaFleet Fleet => _fleet!;

    // ---------------------------------------------------------------
    // Scenario 1 — full happy path (acceptance bullet 1), plus R24's
    // structural causal-order check (acceptance bullet 4, #7's amendment
    // A1).
    // ---------------------------------------------------------------

    /// <summary>
    /// Places a real order over real HTTP through the real Gateway, lets it
    /// flow through the whole saga unattended, registers a real payment
    /// through the SAME HTTP surface, and asserts the order reaches
    /// <c>completed</c>. R24's own subject (never asserted at API level
    /// anywhere in this repository before this feature —
    /// <c>specs/shared/test-matrix.md</c>'s own R24 row names this test as
    /// its closer): the completion triple (<c>payment.received.v1</c>,
    /// <c>credit.released.v1</c>, <c>order.completed.v1</c>) is present in
    /// <c>events[]</c> AND causally ordered, checked STRUCTURALLY via
    /// <see cref="AssertCausalOrder"/> — never a hand-written expected
    /// sequence, which is precisely the gap #7's own review (D4) found: a
    /// scenario asserting only <c>status === 'completed'</c> can go green
    /// while the timeline itself is inverted.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered()
    {
        // #7's own scenario 1 asserts the POST /orders response shape
        // itself (totalAmount, status, projectionPending) before ever
        // polling — ported here directly rather than through the shared
        // PlaceOrderAsync helper (every other scenario in this file uses
        // that helper and does not need this repeated).
        var placeResponse = await Fleet.Gateway.Client.PostAsJsonAsync(
            "/orders",
            new
            {
                retailerCode = SagaFleet.RetailerCode,
                companyCode = SagaFleet.CompanyCode,
                currency = SagaFleet.Currency,
                lines = new[] { new { productCode = SagaFleet.ProductCode, quantity = 2, unitPrice = 1500L } }, // 2 x 1500 = 3000 — not a `.99` total.
            });
        var placeBody = await placeResponse.Content.ReadAsStringAsync();
        Assert.True(placeResponse.StatusCode == HttpStatusCode.Created, $"POST /orders failed: {(int)placeResponse.StatusCode} {placeResponse.StatusCode}: {placeBody}");
        using var placeDoc = JsonDocument.Parse(placeBody);
        Assert.Equal(3000L, placeDoc.RootElement.GetProperty("totalAmount").GetInt64());
        Assert.True(placeDoc.RootElement.GetProperty("projectionPending").GetBoolean());
        Assert.Equal("placed", placeDoc.RootElement.GetProperty("status").GetString());
        var orderId = placeDoc.RootElement.GetProperty("orderId").GetGuid();

        var invoiced = await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));
        Assert.NotNull(invoiced.GetProperty("references").GetProperty("invoiceReference").GetString());

        var orderReference = invoiced.GetProperty("orderReference").GetString()!;
        var invoice = await FindInvoiceAsync(orderReference, TimeSpan.FromSeconds(30));
        Assert.Equal(3000L, invoice.TotalAmount);

        var paymentResponse = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoice.InvoiceId}/payments",
            new
            {
                paymentReference = $"PAY-HAPPY-{Guid.NewGuid():N}"[..24],
                amount = new { amount = invoice.TotalAmount, currency = invoice.Currency },
                valueDate = DateTimeOffset.UtcNow,
                source = "test",
            });
        var paymentBody = await paymentResponse.Content.ReadAsStringAsync();
        Assert.True(paymentResponse.StatusCode == HttpStatusCode.Created, $"POST /invoices/{{id}}/payments failed: {(int)paymentResponse.StatusCode} {paymentResponse.StatusCode}: {paymentBody}");
        using (var paymentDoc = JsonDocument.Parse(paymentBody))
        {
            Assert.Equal("accepted", paymentDoc.RootElement.GetProperty("outcome").GetString());
        }

        var completed = await WaitForOrderStatusAsync(orderId, "completed", TimeSpan.FromSeconds(60));
        Assert.Equal("completed", completed.GetProperty("status").GetString());

        var eventsArray = completed.GetProperty("events");
        var eventTypes = eventsArray.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("payment.received.v1", eventTypes);
        Assert.Contains("credit.released.v1", eventTypes);
        Assert.Contains("order.completed.v1", eventTypes);

        // R24 (structural, amendment A1) — the GENERAL causal-order
        // invariant, applied to every entry this order's timeline carries,
        // not merely the completion triple. id 105 — minimumEdgesChecked: 3
        // is the happy path's OWN known floor, MEASURED directly (never
        // guessed): a real run's nine-entry timeline
        // (order.placed.v1..order.completed.v1, see progress/impl_causal_order_check_is_vacuous_without_causation_id.md)
        // resolves exactly three causationId edges to another entry in the
        // SAME timeline — one of them is order.completed.v1's own edge to
        // credit.released.v1 (SagaStepTable.cs:260); the rest of this
        // timeline's entries name a command id or a cross-service fact
        // this order's own timeline does not carry, which
        // AssertCausalOrder correctly skips rather than asserts.
        AssertCausalOrder(eventsArray, minimumEdgesChecked: 3);
    }

    // ---------------------------------------------------------------
    // Scenario 2 — full compensation path (acceptance bullet 2).
    // ---------------------------------------------------------------

    /// <summary>
    /// R28/R42 — a total whose minor units end in <c>99</c> is refused by
    /// the credit simulator unconditionally. Asserts three separate,
    /// precise facts, matching #7's own scenario 2 and
    /// <see cref="SagaEndToEndVerificationTests.Criterion2_NinetyNineOrder_CompensatesVisibly"/>'s
    /// precedent: (a) three DISTINCT compensation-chain facts
    /// (<c>credit.rejected.v1</c>, <c>stock.released.v1</c>,
    /// <c>order.cancelled.v1</c>) visible in the order's own timeline, IN
    /// CAUSAL ORDER (both the specific chain and the GENERAL structural
    /// invariant); (b) the order's own <c>cancellationReason</c> is
    /// <c>credit_rejected</c> (the domain's closed three-value set); (c)
    /// Fulfillment's OWN reservation row is genuinely <c>released</c>, read
    /// directly from Fulfillment's own database, never inferred from the
    /// order's status field alone.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task NinetyNineOrder_CompensatesVisibly_WithTheCompensationChainCausallyOrdered()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1099, quantity: 1); // 1099 % 100 == 99.

        var cancelled = await WaitForOrderStatusAsync(orderId, "cancelled", TimeSpan.FromSeconds(90));
        Assert.Equal("credit_rejected", cancelled.GetProperty("cancellationReason").GetString());

        var eventsArray = cancelled.GetProperty("events");
        var events = eventsArray.EnumerateArray().ToList();
        var eventTypes = events.Select(e => e.GetProperty("eventType").GetString()).ToList();

        var creditRejectedIndex = eventTypes.IndexOf("credit.rejected.v1");
        var stockReleasedIndex = eventTypes.IndexOf("stock.released.v1");
        var orderCancelledIndex = eventTypes.IndexOf("order.cancelled.v1");
        Assert.True(creditRejectedIndex >= 0, $"expected credit.rejected.v1 in the timeline, got {string.Join(",", eventTypes)}");
        Assert.True(stockReleasedIndex >= 0, $"expected stock.released.v1 in the timeline, got {string.Join(",", eventTypes)}");
        Assert.True(orderCancelledIndex >= 0, $"expected order.cancelled.v1 in the timeline, got {string.Join(",", eventTypes)}");
        Assert.True(creditRejectedIndex < stockReleasedIndex, $"credit.rejected.v1 must precede stock.released.v1 — got indices {creditRejectedIndex}, {stockReleasedIndex}: {string.Join(",", eventTypes)}");
        Assert.True(stockReleasedIndex < orderCancelledIndex, $"stock.released.v1 must precede order.cancelled.v1 in causal order — got indices {stockReleasedIndex}, {orderCancelledIndex}: {string.Join(",", eventTypes)}");

        // The GENERAL structural invariant (amendment A1) — same helper,
        // same claim, applied to the compensation path too.
        AssertCausalOrder(eventsArray);

        // Ported-idiom ledger, documentation half (#7's D2/§5): #8's
        // SagaFactHandler.cs:292 reuses the triggering stock.released.v1
        // fact's OWN OccurredAt verbatim for order.cancelled.v1 (never a
        // fresh clock read), so the two entries are BYTE-IDENTICAL on
        // occurredAt by design — exactly #7's documented property. The
        // array-order assertions above (never this equality) are what
        // actually prove causal order; this only documents the tie so a
        // future reader does not mistake it for the ordering proof.
        var stockReleasedOccurredAt = events[stockReleasedIndex].GetProperty("occurredAt").GetDateTimeOffset();
        var orderCancelledOccurredAt = events[orderCancelledIndex].GetProperty("occurredAt").GetDateTimeOffset();
        Assert.Equal(stockReleasedOccurredAt, orderCancelledOccurredAt);

        // The reservation genuinely released — read from Fulfillment's OWN
        // MS-SQL database, never merely inferred from the Gateway's own
        // read model.
        var orderReference = cancelled.GetProperty("orderReference").GetString()!;
        await using var fulfillmentDb = mssql.CreateDbContext(Fleet.FulfillmentConnectionString);
        var reservations = await fulfillmentDb.Reservations.AsNoTracking()
            .Where(r => r.OrderReference == orderReference)
            .ToListAsync();
        var reservation = Assert.Single(reservations);
        Assert.Equal("released", reservation.Status);
    }

    // ---------------------------------------------------------------
    // Scenario 3 — duplicate paymentReference (acceptance bullet 3, R48).
    // ---------------------------------------------------------------

    /// <summary>
    /// R48/B10 — registering the SAME <c>paymentReference</c> twice against
    /// the real Gateway yields exactly one payment: 201/accepted then
    /// 200/duplicate with <c>Idempotent-Replay: true</c>, and Billing's OWN
    /// database records exactly one <c>payments</c> row for that reference
    /// — never merely the reply shape.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task DuplicatePaymentReference_YieldsExactlyOnePayment_InBillingsOwnDatabase()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 2499, quantity: 3); // 7497 — not `.99`.
        var invoiced = await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));
        var orderReference = invoiced.GetProperty("orderReference").GetString()!;
        var invoice = await FindInvoiceAsync(orderReference, TimeSpan.FromSeconds(30));

        var paymentReference = $"PAY-IDEMP-{Guid.NewGuid():N}"[..24];
        var body = new
        {
            paymentReference,
            amount = new { amount = invoice.TotalAmount, currency = invoice.Currency },
            valueDate = DateTimeOffset.UtcNow,
            source = "test",
        };

        var first = await Fleet.Gateway.Client.PostAsJsonAsync($"/invoices/{invoice.InvoiceId}/payments", body);
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.Created, $"first payment failed: {(int)first.StatusCode} {first.StatusCode}: {firstBody}");
        using (var firstDoc = JsonDocument.Parse(firstBody))
        {
            Assert.Equal("accepted", firstDoc.RootElement.GetProperty("outcome").GetString());
        }

        var second = await Fleet.Gateway.Client.PostAsJsonAsync($"/invoices/{invoice.InvoiceId}/payments", body);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.OK, $"second (duplicate) payment did not answer 200: {(int)second.StatusCode} {second.StatusCode}: {secondBody}");
        using (var secondDoc = JsonDocument.Parse(secondBody))
        {
            Assert.Equal("duplicate", secondDoc.RootElement.GetProperty("outcome").GetString());
        }
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replay").Single());

        // Billing's OWN database — the durable source of truth, never
        // merely the reply shape.
        await using var billingDb = OpenBillingDb();
        var paymentCount = await billingDb.Payments.AsNoTracking()
            .CountAsync(p => p.PaymentReference == paymentReference);
        Assert.Equal(1, paymentCount);
    }

    // ---------------------------------------------------------------
    // Scenario 4 — R49's API half (acceptance bullet 5): a mismatched
    // amount, a mismatched currency and a different paymentReference
    // against an already-paid invoice are each rejected with a
    // machine-readable code, through the REAL Gateway, with Billing's OWN
    // database showing nothing changed. #7 never closed this at API level
    // (specs/shared/test-matrix.md's own R49 row) — #8 closes it first.
    // ---------------------------------------------------------------

    /// <summary>
    /// A payment whose <c>amount</c> does not equal the invoice's own
    /// <c>totalAmount</c> is refused. Billing's
    /// <see cref="OrderToCash.Billing.Domain.Errors.InvoicePaymentAmountMismatchError"/>
    /// carries <c>details.code = INVOICE_PAYMENT_AMOUNT_MISMATCH</c>
    /// (<c>BillingErrorMapper.cs:95</c>), which
    /// <c>RpcErrorClassifier</c>'s override table
    /// (<c>src/Gateway/Domain/Problem/RpcErrorClassification.cs:63</c>)
    /// answers as HTTP 422, <c>Problem.code = PAYMENT_MISMATCH</c>.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task R49_AMismatchedAmount_IsRejectedWithAMachineReadableCode_AndBillingsOwnDatabaseShowsNothingChanged()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1200, quantity: 1);
        var invoiced = await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));
        var orderReference = invoiced.GetProperty("orderReference").GetString()!;
        var invoice = await FindInvoiceAsync(orderReference, TimeSpan.FromSeconds(30));

        // Baseline BEFORE the rejected attempt — placing an order already
        // writes a `credit_items` "hold" row (R41/BC — the credit approval
        // that let the saga reach `invoiced` in the first place), so "no
        // credit-ledger row for this order at all" is the WRONG claim; the
        // right one is "the rejected attempt adds none".
        var (_, creditItemCountBefore) = await ReadBillingCountsAsync(invoice.InvoiceId, orderReference);

        var response = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoice.InvoiceId}/payments",
            new
            {
                paymentReference = $"PAY-BADAMT-{Guid.NewGuid():N}"[..24],
                amount = new { amount = invoice.TotalAmount + 1, currency = invoice.Currency }, // wrong on purpose.
                valueDate = DateTimeOffset.UtcNow,
                source = "test",
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"code\":\"PAYMENT_MISMATCH\"", body, StringComparison.Ordinal);

        await AssertBillingDatabaseUnchangedAsync(invoice.InvoiceId, orderReference, expectedPaymentCount: 0, expectedInvoiceStatus: "issued", expectedCreditItemCount: creditItemCountBefore);
    }

    /// <summary>
    /// A payment whose <c>currency</c> does not match the invoice's own is
    /// refused. Same classification path as the amount mismatch —
    /// <c>INVOICE_PAYMENT_CURRENCY_MISMATCH</c> → 422/<c>PAYMENT_MISMATCH</c>.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task R49_AMismatchedCurrency_IsRejectedWithAMachineReadableCode_AndBillingsOwnDatabaseShowsNothingChanged()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1300, quantity: 1);
        var invoiced = await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));
        var orderReference = invoiced.GetProperty("orderReference").GetString()!;
        var invoice = await FindInvoiceAsync(orderReference, TimeSpan.FromSeconds(30));
        Assert.Equal(SagaFleet.Currency, invoice.Currency); // EUR — the mismatch below must be genuine.

        var (_, creditItemCountBefore) = await ReadBillingCountsAsync(invoice.InvoiceId, orderReference);

        var response = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoice.InvoiceId}/payments",
            new
            {
                paymentReference = $"PAY-BADCCY-{Guid.NewGuid():N}"[..24],
                amount = new { amount = invoice.TotalAmount, currency = "USD" }, // wrong on purpose.
                valueDate = DateTimeOffset.UtcNow,
                source = "test",
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"code\":\"PAYMENT_MISMATCH\"", body, StringComparison.Ordinal);

        await AssertBillingDatabaseUnchangedAsync(invoice.InvoiceId, orderReference, expectedPaymentCount: 0, expectedInvoiceStatus: "issued", expectedCreditItemCount: creditItemCountBefore);
    }

    /// <summary>
    /// A SECOND, DIFFERENT <c>paymentReference</c> against an invoice that
    /// is already <c>paid</c> is refused — <c>Invoice.MarkPaid</c>'s own
    /// unconditional B8 guard (<c>InvoiceAlreadyPaidError</c>, thrown
    /// BEFORE the amount/currency checks, <c>Invoice.cs:321-324</c>) fires
    /// regardless of what the new reference names. Classified
    /// <c>INVOICE_ALREADY_PAID</c> → HTTP 409
    /// (<c>RpcErrorClassification.cs:62</c>). Never the SAME reference
    /// replayed — that is R48's idempotent-duplicate scenario above, and
    /// deliberately a different HTTP status (200) and a different
    /// <c>Problem.code</c>.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task R49_ADifferentPaymentReferenceAgainstAnAlreadyPaidInvoice_IsRejectedWithAMachineReadableCode_AndBillingsOwnDatabaseShowsNothingElseChanged()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1400, quantity: 1);
        var invoiced = await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));
        var orderReference = invoiced.GetProperty("orderReference").GetString()!;
        var invoice = await FindInvoiceAsync(orderReference, TimeSpan.FromSeconds(30));

        var firstReference = $"PAY-FIRST-{Guid.NewGuid():N}"[..24];
        var firstResponse = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoice.InvoiceId}/payments",
            new
            {
                paymentReference = firstReference,
                amount = new { amount = invoice.TotalAmount, currency = invoice.Currency },
                valueDate = DateTimeOffset.UtcNow,
                source = "test",
            });
        Assert.True(firstResponse.StatusCode == HttpStatusCode.Created, $"the priming payment failed: {(int)firstResponse.StatusCode} {firstResponse.StatusCode}: {await firstResponse.Content.ReadAsStringAsync()}");

        // The state right after the ONE genuine payment — the baseline the
        // rejected second attempt below must not move.
        var (paymentCountAfterFirst, creditItemCountAfterFirst) = await ReadBillingCountsAsync(invoice.InvoiceId, orderReference);
        Assert.Equal(1, paymentCountAfterFirst);

        var secondReference = $"PAY-SECOND-{Guid.NewGuid():N}"[..24];
        var secondResponse = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoice.InvoiceId}/payments",
            new
            {
                paymentReference = secondReference, // DIFFERENT from firstReference on purpose.
                amount = new { amount = invoice.TotalAmount, currency = invoice.Currency },
                valueDate = DateTimeOffset.UtcNow,
                source = "test",
            });
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Contains("\"code\":\"INVOICE_ALREADY_PAID\"", secondBody, StringComparison.Ordinal);

        var (paymentCountAfterSecond, creditItemCountAfterSecond) = await ReadBillingCountsAsync(invoice.InvoiceId, orderReference);
        Assert.Equal(paymentCountAfterFirst, paymentCountAfterSecond);
        Assert.Equal(creditItemCountAfterFirst, creditItemCountAfterSecond);

        await using var billingDb = OpenBillingDb();
        var secondReferenceRowCount = await billingDb.Payments.AsNoTracking().CountAsync(p => p.PaymentReference == secondReference);
        Assert.Equal(0, secondReferenceRowCount); // the rejected reference itself was never written.
    }

    private async Task AssertBillingDatabaseUnchangedAsync(Guid invoiceId, string orderReference, int expectedPaymentCount, string expectedInvoiceStatus, int expectedCreditItemCount)
    {
        await using var billingDb = OpenBillingDb();
        var invoiceRow = await billingDb.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(expectedInvoiceStatus, invoiceRow.Status);
        Assert.Null(invoiceRow.PaidAt);

        var paymentCount = await billingDb.Payments.AsNoTracking().CountAsync(p => p.InvoiceId == invoiceId);
        Assert.Equal(expectedPaymentCount, paymentCount);

        // The BASELINE count, captured before the rejected attempt — never
        // a bare zero. Placing the order already wrote a "hold" row
        // (R41/BC, the credit approval the saga needed to reach
        // `invoiced`); what the rejection must not do is add a "release"
        // row (or any other row) on top of it.
        var creditItemCount = await billingDb.CreditItems.AsNoTracking().CountAsync(c => c.OrderReference == orderReference);
        Assert.Equal(expectedCreditItemCount, creditItemCount);
    }

    private async Task<(int PaymentCount, int CreditItemCount)> ReadBillingCountsAsync(Guid invoiceId, string orderReference)
    {
        await using var billingDb = OpenBillingDb();
        var paymentCount = await billingDb.Payments.AsNoTracking().CountAsync(p => p.InvoiceId == invoiceId);
        var creditItemCount = await billingDb.CreditItems.AsNoTracking().CountAsync(c => c.OrderReference == orderReference);
        return (paymentCount, creditItemCount);
    }

    // ---------------------------------------------------------------
    // R24 (amendment A1) — the GENERAL causal-order invariant, structural,
    // never a hand-written expected sequence per scenario. Ported from #7's
    // black-box-api.integration.spec.ts:151-164 (assertCausalOrder).
    // ---------------------------------------------------------------

    /// <summary>
    /// For every entry <c>effect</c> whose <c>causationId</c> names another
    /// entry <c>cause</c>'s own <c>eventId</c> IN THE SAME TIMELINE,
    /// <c>cause</c> must precede <c>effect</c>. A <c>causationId</c> naming
    /// nothing in this order's own timeline (a command id, or a fact
    /// outside this order entirely) is skipped, not asserted — matching
    /// #7's amendment A1 exactly.
    /// </summary>
    /// <param name="events">The order's own <c>events[]</c> timeline.</param>
    /// <param name="minimumEdgesChecked">
    /// id 105 (N1 of the id 31 review) — the loop below can run to
    /// completion having asserted NOTHING, if every entry's
    /// <c>causationId</c> is absent, unparsable, or names nothing in this
    /// timeline (exactly what would happen if the Gateway ever stopped
    /// mapping <c>causationId</c> at all, <c>MongoOrderReadModel.cs:132</c>).
    /// Defaults to 1 — "at least one real edge was checked" — so no caller
    /// can go green on zero by omission; a caller with domain knowledge of
    /// a larger floor (see <see cref="HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered"/>'s
    /// call site) passes it explicitly.
    /// </param>
    private static void AssertCausalOrder(JsonElement events, int minimumEdgesChecked = 1)
    {
        var list = events.EnumerateArray().ToList();
        var indexOfEventId = new Dictionary<Guid, int>();
        for (var i = 0; i < list.Count; i++)
        {
            indexOfEventId[list[i].GetProperty("eventId").GetGuid()] = i;
        }

        var edgesChecked = 0;

        for (var effectIndex = 0; effectIndex < list.Count; effectIndex++)
        {
            if (!list[effectIndex].TryGetProperty("causationId", out var causationIdProp) || causationIdProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var causationId = causationIdProp.GetGuid();
            if (!indexOfEventId.TryGetValue(causationId, out var causeIndex))
            {
                continue;
            }

            edgesChecked++;

            Assert.True(
                causeIndex < effectIndex,
                $"causal order violated: {list[effectIndex].GetProperty("eventType").GetString()} (index {effectIndex}) " +
                $"names causationId {causationId}, but its cause (index {causeIndex}) does not precede it — " +
                $"events: {string.Join(",", list.Select(e => e.GetProperty("eventType").GetString()))}");
        }

        // id 105 — a helper that can pass on zero checked edges is vacuous:
        // it would stay green even if causationId stopped reaching the wire
        // entirely. The message below deliberately says "no causal edges
        // were checked" so a failure here can never be mistaken for an
        // ordering violation (the message above).
        var eventTypes = string.Join(",", list.Select(e => e.GetProperty("eventType").GetString()));
        Assert.True(
            edgesChecked >= minimumEdgesChecked,
            edgesChecked == 0
                ? $"no causal edges were checked — every entry's causationId was absent, unparsable, or named nothing in this timeline: {eventTypes}"
                : $"AssertCausalOrder checked only {edgesChecked} causal edge(s), fewer than the required minimum {minimumEdgesChecked} — events: {eventTypes}");
    }

    /// <summary>
    /// Meta-guard for <see cref="AssertCausalOrder"/> itself — the brief's
    /// "swap two timeline entries and the check must fail" arm. No fleet
    /// needed: a hand-built, adversarial two-entry array where the SECOND
    /// array element is the CAUSE of the FIRST — the exact inversion #7's
    /// own review (D4) found live and amendment A1 exists to catch.
    /// </summary>
    [Fact]
    public void AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation()
    {
        const string invertedTimelineJson = """
            [
              { "eventId": "11111111-1111-1111-1111-111111111111", "eventType": "b.happened.v1", "occurredAt": "2026-01-01T00:00:00Z", "causationId": "22222222-2222-2222-2222-222222222222" },
              { "eventId": "22222222-2222-2222-2222-222222222222", "eventType": "a.happened.v1", "occurredAt": "2026-01-01T00:00:00Z" }
            ]
            """;
        using var doc = JsonDocument.Parse(invertedTimelineJson);

        var failure = Assert.Throws<Xunit.Sdk.TrueException>(() => AssertCausalOrder(doc.RootElement));
        Assert.Contains("causal order violated", failure.Message, StringComparison.Ordinal);
        Assert.Contains("b.happened.v1", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// id 105 (N1 of the id 31 review) — the meta-guard for the helper's
    /// OWN vacuity: a timeline whose entries carry no <c>causationId</c> at
    /// all (exactly what the Gateway would produce if
    /// <c>MongoOrderReadModel.cs:132</c> ever stopped mapping it) must make
    /// <see cref="AssertCausalOrder"/> fail, naming the zero count — never
    /// pass silently on having asserted nothing. No fleet needed.
    /// </summary>
    [Fact]
    public void AssertCausalOrder_WhenNoEntryCarriesACausationId_FailsNamingNoEdgesChecked()
    {
        const string causationlessTimelineJson = """
            [
              { "eventId": "11111111-1111-1111-1111-111111111111", "eventType": "a.happened.v1", "occurredAt": "2026-01-01T00:00:00Z" },
              { "eventId": "22222222-2222-2222-2222-222222222222", "eventType": "b.happened.v1", "occurredAt": "2026-01-01T00:00:01Z" }
            ]
            """;
        using var doc = JsonDocument.Parse(causationlessTimelineJson);

        var failure = Assert.Throws<Xunit.Sdk.TrueException>(() => AssertCausalOrder(doc.RootElement));
        Assert.Contains("no causal edges were checked", failure.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------
    // Helpers — duplicated from SagaEndToEndVerificationTests rather than
    // shared, matching that class's own stated rationale (this file is a
    // sibling scenario suite, not a caller of it).
    // ---------------------------------------------------------------

    private async Task<Guid> PlaceOrderAsync(long unitPrice, int quantity)
    {
        var response = await Fleet.Gateway.Client.PostAsJsonAsync(
            "/orders",
            new
            {
                retailerCode = SagaFleet.RetailerCode,
                companyCode = SagaFleet.CompanyCode,
                currency = SagaFleet.Currency,
                lines = new[] { new { productCode = SagaFleet.ProductCode, quantity, unitPrice } },
            });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST /orders failed: {(int)response.StatusCode} {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("orderId").GetGuid();
    }

    private async Task<JsonElement> WaitForOrderStatusAsync(Guid orderId, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? lastStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await Fleet.Gateway.Client.GetAsync($"/orders/{orderId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                lastStatus = doc.RootElement.GetProperty("status").GetString();
                if (lastStatus == expectedStatus)
                {
                    return doc.RootElement.Clone();
                }
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"order {orderId} never reached status \"{expectedStatus}\" within {timeout} (last observed: \"{lastStatus ?? "none/pending"}\").");
    }

    private sealed record InvoiceInfo(Guid InvoiceId, long TotalAmount, string Currency);

    /// <summary><c>POST /invoices/{id}/payments</c> requires Billing's internal <c>invoiceId</c> — resolved the way the Gateway itself resolves it: <c>GET /invoices?orderReference=...</c>, over HTTP, never a direct DB read.</summary>
    private async Task<InvoiceInfo> FindInvoiceAsync(string orderReference, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await Fleet.Gateway.Client.GetAsync($"/invoices?orderReference={orderReference}");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() > 0)
            {
                var item = items[0];
                return new InvoiceInfo(item.GetProperty("invoiceId").GetGuid(), item.GetProperty("totalAmount").GetInt64(), item.GetProperty("currency").GetString()!);
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"no invoice ever appeared for order reference \"{orderReference}\" within {timeout}.");
    }

    private BillingDbContext OpenBillingDb() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(Fleet.BillingConnectionString).Options);
}

/// <summary>
/// <see cref="BlackBoxApiTests"/>'s own teardown timing fixture — the exact
/// analogue of <see cref="SagaFleetTeardown"/>, needed because this class
/// holds its OWN statically-built <see cref="SagaFleet"/>, separate from
/// <see cref="SagaEndToEndVerificationTests"/>'s.
/// </summary>
public sealed class BlackBoxApiFleetTeardown : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => BlackBoxApiTests.TearDownFleetIfBuiltAsync();
}

/// <summary>
/// A SEPARATE collection from <see cref="SagaE2ECollection"/>, deliberately
/// — see this file's own class remarks for the measured reason (two live
/// <see cref="SagaFleet"/> instances sharing one collection produced 5
/// spurious timeouts in the OTHER class). Its own four container fixtures
/// (a SECOND Kafka/MS-SQL-server/NATS/MongoDB, distinct from
/// <see cref="SagaE2ECollection"/>'s) cost real extra container-boot time,
/// accepted deliberately in exchange for xUnit fully finishing and
/// DISPOSING <see cref="SagaE2ECollection"/> before this one starts
/// (<c>AssemblyBehavior.cs</c>'s assembly-wide
/// <c>DisableTestParallelization = true</c> serialises collections, never
/// classes within one).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BlackBoxApiCollection :
    ICollectionFixture<KafkaContainerFixture>, ICollectionFixture<MsSqlContainerFixture>,
    ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MongoContainerFixture>,
    ICollectionFixture<BlackBoxApiFleetTeardown>
{
    public const string Name = "GatewayBlackBoxApi";
}
