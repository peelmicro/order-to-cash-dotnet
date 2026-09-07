using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// `R47` – `R49` integration half, over the REAL responder, real MS-SQL,
/// real NATS and real Kafka. This is the sole LIVE caller of
/// <c>Invoice.MarkPaid</c> and <c>BuyerCredit.Release</c>, both delivered
/// uncalled by features 21/19 — this file is what proves their existing
/// guards hold through the live path, not only through their unit tests.
/// Every reply assertion opens the body and asserts its own discriminating
/// field before touching a collection (backlog id 55's shape).
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class PaymentRegisterTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-r47");
        using var _ = host;

        const string orderReference = "ORD-000301";
        const long total = 7_500;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "consume");
        var invoiceId = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900301", orderReference, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        var availableCreditBefore = await AvailableCreditAsync(creditId, connectionString);
        var outboxCountBefore = await WholeTableOutboxCountAsync(connectionString);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();
        var headers = BuildHeaders(correlationId, requestId);
        var valueDate = DateTimeOffset.UtcNow;
        var request = BillingHostFixture.PaymentRequest("PAY-000301", total, valueDate, invoiceReference: "INV-900301");

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), headers);
        var payload = RpcJson.Deserialize<PaymentRegisterReplyPayload>(reply.Data!);

        Assert.Equal("accepted", payload.Outcome);
        Assert.Equal("PAY-000301", payload.PaymentReference);
        Assert.Equal("INV-900301", payload.InvoiceReference);
        Assert.Equal(orderReference, payload.OrderReference);
        Assert.Equal("paid", payload.InvoiceStatus);
        Assert.NotNull(payload.PaidAt);

        // The invoice row.
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var invoiceRow = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal("paid", invoiceRow.Status);
            Assert.NotNull(invoiceRow.PaidAt);
        }

        // The payments row.
        var paymentRows = await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceId);
        var paymentRow = Assert.Single(paymentRows);
        Assert.Equal("PAY-000301", paymentRow.PaymentReference);
        Assert.Equal(total, paymentRow.Amount);
        Assert.Equal("EUR", paymentRow.CurrencyCode);
        Assert.Equal("test", paymentRow.Source);

        // The credit ledger — a release entry equal to the hold, and
        // availableCredit returns to exactly where it started before the
        // hold (the ledger identity: exposure(order) -> 0).
        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference);
        var releaseEntries = ledger.Where(e => e.Type == "release").ToList();
        var releaseEntry = Assert.Single(releaseEntries);
        Assert.Equal(total, releaseEntry.Amount);

        var availableCreditAfter = await AvailableCreditAsync(creditId, connectionString);
        Assert.Equal(availableCreditBefore + total, availableCreditAfter);

        // R47's ordering — payment.received.v1 THEN credit.released.v1, in
        // that order, both in the SAME transaction (same correlation id).
        var outboxCountAfter = await WholeTableOutboxCountAsync(connectionString);
        Assert.Equal(outboxCountBefore + 2, outboxCountAfter);

        var factRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Equal(2, factRows.Count);
        var orderedFacts = factRows.OrderBy(f => f.Seq).ToList();
        Assert.Equal("payment.received.v1", orderedFacts[0].EventType);
        Assert.Equal("credit.released.v1", orderedFacts[1].EventType);
        Assert.True(orderedFacts[0].Seq < orderedFacts[1].Seq);

        // Backlog id 57 (ported from #7's `bf59af9`) — the causal EDGE, on
        // the envelope itself, not merely the outbox's `seq` ordering
        // asserted above. `credit.released.v1`'s `causationId` must be
        // `payment.received.v1`'s OWN `eventId` — before this fix both
        // rows carried `causationId = requestId.value` and were siblings a
        // consumer reading only the two envelopes could not causally
        // order. Reads the REAL `eventId` the live path assigned; never
        // merely asserts the two ids differ (provenance, not
        // non-collision).
        Assert.Equal(orderedFacts[0].EventId, orderedFacts[1].CausationId);
        Assert.NotEqual(requestId.Value, orderedFacts[1].CausationId);
        // payment.received.v1 itself is unaffected — it still carries the
        // request's own id (only the SIBLING relationship changes).
        Assert.Equal(requestId.Value, orderedFacts[0].CausationId);

        // The background relay (100ms poll, per StartHostAsync) publishes
        // asynchronously — wait for BOTH rows to be stamped rather than
        // asserting on a race against it.
        var bothPublished = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value),
            rows => rows.All(r => r.PublishedAt is not null),
            TimeSpan.FromSeconds(10));
        Assert.All(bothPublished, r => Assert.NotNull(r.PublishedAt));

        // The published payload's fields equal the REQUEST's, field by
        // field — not merely a row count (feature 17's own lesson).
        var paymentReceivedPayload = System.Text.Json.JsonSerializer.Deserialize<PaymentReceivedPayload>(orderedFacts[0].Payload, JsonWire.Options)!;
        Assert.Equal(orderReference, paymentReceivedPayload.OrderReference);
        Assert.Equal("INV-900301", paymentReceivedPayload.InvoiceReference);
        Assert.Equal("PAY-000301", paymentReceivedPayload.PaymentReference);
        Assert.Equal("EUR", paymentReceivedPayload.Currency);
        Assert.Equal(total, paymentReceivedPayload.Amount);
        Assert.Equal("test", paymentReceivedPayload.Source);

        var creditReleasedPayload = System.Text.Json.JsonSerializer.Deserialize<CreditReleasedPayload>(orderedFacts[1].Payload, JsonWire.Options)!;
        Assert.Equal(orderReference, creditReleasedPayload.OrderReference);
        Assert.Equal("CarrefourEs", creditReleasedPayload.RetailerCode);
        Assert.Equal("IBERFOODS", creditReleasedPayload.CompanyCode);
        Assert.Equal("EUR", creditReleasedPayload.Currency);
        Assert.Equal(total, creditReleasedPayload.ReleasedAmount);
        Assert.Equal("invoice_paid", creditReleasedPayload.Reason);

        await host.StopAsync();
    }

    [Fact]
    public async Task R48_ASequentialRepeatOfTheSamePaymentReferenceAnswersDuplicate_RecordsNoSecondPayment_AndEmitsNoSecondFact()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-r48-sequential");
        using var _ = host;

        const string orderReference = "ORD-000302";
        const long total = 3_300;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "hold");
        var invoiceId = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900302", orderReference, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var valueDate = DateTimeOffset.UtcNow;
        var request = BillingHostFixture.PaymentRequest("PAY-000302", total, valueDate, invoiceReference: "INV-900302");

        var firstReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var firstPayload = RpcJson.Deserialize<PaymentRegisterReplyPayload>(firstReply.Data!);
        Assert.Equal("accepted", firstPayload.Outcome);

        var outboxCountBeforeRepeat = await WholeTableOutboxCountAsync(connectionString);

        var repeatReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var repeatPayload = RpcJson.Deserialize<PaymentRegisterReplyPayload>(repeatReply.Data!);

        Assert.Equal("duplicate", repeatPayload.Outcome);
        Assert.Equal(firstPayload.InvoiceReference, repeatPayload.InvoiceReference);
        Assert.Equal(firstPayload.PaidAt, repeatPayload.PaidAt);
        Assert.Equal("paid", repeatPayload.InvoiceStatus);

        var paymentRows = await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceId);
        Assert.Single(paymentRows);

        Assert.Equal(outboxCountBeforeRepeat, await WholeTableOutboxCountAsync(connectionString));

        await host.StopAsync();
    }

    [Fact]
    public async Task R48_TwoConcurrentRequestsForTheSamePaymentReferenceProduceExactlyOnePaymentRow_AndExactlyOneFactPair()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-r48-concurrent");
        using var _ = host;

        const string orderReference = "ORD-000303";
        const long total = 4_400;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "hold");
        var invoiceId = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900303", orderReference, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
        await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });
        var valueDate = DateTimeOffset.UtcNow;
        var request = BillingHostFixture.PaymentRequest("PAY-000303", total, valueDate, invoiceReference: "INV-900303");
        var body = RpcJson.Serialize(request);

        var taskA = BillingHostFixture.RequestBareAsync(connectionA, InvoiceSubjects.PaymentRegister, body, BuildHeaders(UniqueId.New(), UniqueId.New()));
        var taskB = BillingHostFixture.RequestBareAsync(connectionB, InvoiceSubjects.PaymentRegister, body, BuildHeaders(UniqueId.New(), UniqueId.New()));
        var replies = await Task.WhenAll(taskA, taskB);

        var outcomeA = RpcJson.Deserialize<PaymentRegisterReplyPayload>(replies[0].Data!).Outcome;
        var outcomeB = RpcJson.Deserialize<PaymentRegisterReplyPayload>(replies[1].Data!).Outcome;
        var outcomes = new[] { outcomeA, outcomeB }.OrderBy(o => o, StringComparer.Ordinal).ToArray();
        Assert.Equal(["accepted", "duplicate"], outcomes);

        var paymentRows = await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceId);
        Assert.Single(paymentRows);

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var factCount = await db.OutboxMessages.CountAsync(m => m.EventType == "payment.received.v1" || m.EventType == "credit.released.v1");
            Assert.Equal(2, factCount);
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task N11_TheSamePaymentReferenceReusedAgainstADifferentInvoice_RepliesPreconditionFailedConflict_LeavesTheSecondInvoiceUnpaid_AndCommitsExactlyOnePayment()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-n11");
        using var _ = host;

        const string orderReferenceA = "ORD-000304";
        const string orderReferenceB = "ORD-000305";
        const long total = 1_200;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReferenceA, total, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReferenceB, total, "hold");
        var invoiceIdA = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900304", orderReferenceA, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);
        var invoiceIdB = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900305", orderReferenceB, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var valueDate = DateTimeOffset.UtcNow;

        var firstRequest = BillingHostFixture.PaymentRequest("PAY-000304", total, valueDate, invoiceReference: "INV-900304");
        var firstReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(firstRequest), BuildHeaders(UniqueId.New(), UniqueId.New()));
        Assert.Equal("accepted", RpcJson.Deserialize<PaymentRegisterReplyPayload>(firstReply.Data!).Outcome);

        // The SAME paymentReference, now against a DIFFERENT invoice —
        // sequentially, no race. N11's fix: this must NOT be a
        // success-shaped duplicate naming INV-900304.
        var secondRequest = BillingHostFixture.PaymentRequest("PAY-000304", total, valueDate, invoiceReference: "INV-900305");
        var secondReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(secondRequest), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(secondReply.Data!);

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.NotEqual("CONFLICT", error.Code);
        Assert.Equal("PAY-000304", error.Details?["paymentReference"]?.ToString());

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var invoiceBRow = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceIdB);
            Assert.Equal("issued", invoiceBRow.Status);
            Assert.Null(invoiceBRow.PaidAt);
        }

        Assert.Single(await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceIdA));
        Assert.Empty(await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceIdB));

        await host.StopAsync();
    }

    [Fact]
    public async Task TheBeltAndBracesUniqueConstraintBackstopCatchesTheRaceWhenTwoDifferentInvoicesUnderDifferentCreditLinesShareThePaymentReferenceConcurrently()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-backstop");
        using var _ = host;

        // TWO DIFFERENT credit lines (different retailer/company pairs) —
        // deliberately, so BI8's credit-row lock does NOT serialise the two
        // requests (as it would if they shared one credit line, per the
        // "genuine concurrent duplicate" test above). Only the
        // payments.payment_reference UNIQUE constraint can arbitrate here.
        const string orderReferenceA = "ORD-000306";
        const string orderReferenceB = "ORD-000307";
        const long total = 2_200;
        var creditIdA = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var creditIdB = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000002", "AuchanFr", "LACTALIS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditIdA, orderReferenceA, total, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditIdB, orderReferenceB, total, "hold");
        var invoiceIdA = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900306", orderReferenceA, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);
        var invoiceIdB = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900307", orderReferenceB, "AuchanFr", "LACTALIS", total, 0, total, "issued", paidAt: null);

        await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
        await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });
        var valueDate = DateTimeOffset.UtcNow;

        var requestA = BillingHostFixture.PaymentRequest("PAY-000306", total, valueDate, invoiceReference: "INV-900306");
        var requestB = BillingHostFixture.PaymentRequest("PAY-000306", total, valueDate, invoiceReference: "INV-900307");

        var taskA = BillingHostFixture.RequestBareAsync(connectionA, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(requestA), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var taskB = BillingHostFixture.RequestBareAsync(connectionB, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(requestB), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var replies = await Task.WhenAll(taskA, taskB);

        var paymentsA = await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceIdA);
        var paymentsB = await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceIdB);
        Assert.Equal(1, paymentsA.Count + paymentsB.Count);

        // The LOSER's reply is a genuine error — PRECONDITION_FAILED,
        // DELIBERATELY never CONFLICT (`BC27`) — not a silently-swallowed
        // duplicate and not a raw driver error surfaced as UNAVAILABLE.
        // Discriminates the reply's OWN field first (backlog id 55's
        // shape), never deserialising straight into the typed payload.
        var codes = new List<string>();
        foreach (var reply in replies)
        {
            using var document = System.Text.Json.JsonDocument.Parse(reply.Data!);
            if (document.RootElement.TryGetProperty("code", out var codeElement))
            {
                codes.Add(codeElement.GetString()!);
            }
            else
            {
                Assert.Equal("accepted", document.RootElement.GetProperty("outcome").GetString());
            }
        }

        var loserCode = Assert.Single(codes);
        Assert.Equal("PRECONDITION_FAILED", loserCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task R49_AnAmountMismatchRepliesPreconditionFailed_LeavesTheInvoiceAndTheLedgerUnchanged_AndEmitsNoFact()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-r49-amount");
        using var _ = host;

        const string orderReference = "ORD-000308";
        const long total = 6_600;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "hold");
        var invoiceId = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900308", orderReference, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        var ledgerBefore = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference);
        var outboxCountBefore = await WholeTableOutboxCountAsync(connectionString);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = BillingHostFixture.PaymentRequest("PAY-000308", total - 300, DateTimeOffset.UtcNow, invoiceReference: "INV-900308");

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.Equal("INVOICE_PAYMENT_AMOUNT_MISMATCH", error.Details?["code"]?.ToString());

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var invoiceRow = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal("issued", invoiceRow.Status);
            Assert.Null(invoiceRow.PaidAt);
        }

        Assert.Empty(await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceId));
        Assert.Equal(ledgerBefore.Count, (await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference)).Count);
        Assert.Equal(outboxCountBefore, await WholeTableOutboxCountAsync(connectionString));

        await host.StopAsync();
    }

    [Fact]
    public async Task R49_ACurrencyMismatchRepliesPreconditionFailed_AndWritesNothing()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-r49-currency");
        using var _ = host;

        const string orderReference = "ORD-000309";
        const long total = 8_800;
        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, total, "hold");
        var invoiceId = await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900309", orderReference, "CarrefourEs", "IBERFOODS", total, 0, total, "issued", paidAt: null);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = BillingHostFixture.PaymentRequest("PAY-000309", total, DateTimeOffset.UtcNow, invoiceReference: "INV-900309", currency: "GBP");

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.Equal("INVOICE_PAYMENT_CURRENCY_MISMATCH", error.Details?["code"]?.ToString());

        Assert.Empty(await BillingHostFixture.PaymentsOfAsync(mssql, connectionString, invoiceId));

        await host.StopAsync();
    }

    [Fact]
    public async Task RepliesNotFound_WhenNoInvoiceResolvesForTheNamedInvoiceReference()
    {
        var (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-not-found");
        using var _dispose = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = BillingHostFixture.PaymentRequest("PAY-000310", 1_000, DateTimeOffset.UtcNow, invoiceReference: "INV-999999");

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("INV-999999", error.Details?["invoiceReference"]?.ToString());

        await host.StopAsync();
    }

    [Fact]
    public async Task RepliesValidationFailed_AndWritesNothing_WhenNeitherInvoiceIdNorInvoiceReferenceIsSupplied()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "payment-validation");
        using var _ = host;

        var outboxCountBefore = await WholeTableOutboxCountAsync(connectionString);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = new PaymentRegisterRequestPayload("PAY-000311", new CreditMoney(1_000, "EUR"), DateTimeOffset.UtcNow, "test");

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.PaymentRegister, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Equal(outboxCountBefore, await WholeTableOutboxCountAsync(connectionString));

        await host.StopAsync();
    }

    private async Task<long> AvailableCreditAsync(Guid creditId, string connectionString)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var creditRow = await db.Credits.AsNoTracking().SingleAsync(c => c.Id == creditId);
        var committedExposure = await BillingHostFixture.CommittedExposureOfAsync(mssql, connectionString, creditId);
        return creditRow.CreditLimit - committedExposure;
    }

    private async Task<int> WholeTableOutboxCountAsync(string connectionString)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.OutboxMessages.CountAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };
}
