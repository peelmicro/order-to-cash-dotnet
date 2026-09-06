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
/// `R45` integration half, `BI2` – `BI6`, `BI9` — over the REAL responder,
/// real MS-SQL, real NATS, real Kafka. Every reply assertion opens the body
/// and asserts its own discriminating field before touching a collection
/// (backlog id 55's shape, `BI30`).
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class InvoiceIssueTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task R45_CreatesExactlyOneIssuedInvoiceMirroringTheDespatchedLinesWithANonNegativeTotal_TheIntegrationHalf()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-r45");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        const string orderReference = "ORD-000201";
        // Amount (9_000), discount (1_000, NON-ZERO) and totalAmount (8_000) are
        // three DISTINCT numbers — a zero discount would let the request's
        // `discount` field be dropped without any assertion below noticing
        // (D2: `IssueRequest` maps `discount == 0` to `null`, so a zero never
        // reaches the wire at all). The hold is seeded at the discounted total
        // so the consume ledger entry is also a request-derived value, not an
        // incidental constant.
        const long grossAmount = 9_000;
        const long discount = 1_000;
        const long totalAmount = grossAmount - discount;
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, totalAmount, "hold");

        var availableCreditBefore = await AvailableCreditAsync(creditId, connectionString);
        var outboxCountBefore = await WholeTableOutboxCountAsync(connectionString);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = BillingHostFixture.IssueRequest(
            orderReference,
            "CarrefourEs",
            "IBERFOODS",
            [("SKU-1", 3, 2_000), ("SKU-2", 1, 3_000)],
            discount: discount);

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(request), headers);
        var payload = RpcJson.Deserialize<InvoiceIssueReplyPayload>(reply.Data!);

        Assert.True(payload.Created);
        Assert.Equal("issued", payload.Status);
        Assert.Equal(orderReference, payload.OrderReference);
        Assert.Matches("^INV-[0-9]{6,}$", payload.InvoiceReference);
        Assert.Equal(totalAmount, payload.TotalAmount);

        var invoiceRows = await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference);
        var invoiceRow = Assert.Single(invoiceRows);
        Assert.Equal(payload.InvoiceReference, invoiceRow.InvoiceReference);
        Assert.Equal(grossAmount, invoiceRow.Amount);
        Assert.Equal(discount, invoiceRow.Discount);
        Assert.Equal(totalAmount, invoiceRow.TotalAmount);

        var lineRows = await BillingHostFixture.InvoiceItemsOfAsync(mssql, connectionString, invoiceRow.Id);
        Assert.Equal(2, lineRows.Count);
        Assert.Contains(lineRows, l => l.ProductCode == "SKU-1" && l.Units == 3 && l.Price == 2_000);
        Assert.Contains(lineRows, l => l.ProductCode == "SKU-2" && l.Units == 1 && l.Price == 3_000);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference);
        var consumeEntries = ledger.Where(e => e.Type == "consume").ToList();
        var consumeEntry = Assert.Single(consumeEntries);
        Assert.Equal(totalAmount, consumeEntry.Amount);

        var outboxCountAfter = await WholeTableOutboxCountAsync(connectionString);
        Assert.Equal(outboxCountBefore + 1, outboxCountAfter);

        var factRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "invoice.issued.v1");
        var factRow = Assert.Single(factRows);
        var factPayload = System.Text.Json.JsonSerializer.Deserialize<InvoiceIssuedPayload>(factRow.Payload, JsonWire.Options)!;
        Assert.Equal(2, factPayload.Lines.Count);
        // The published payload's fields equal the REQUEST's, field by
        // field — not merely a row count (feature 17's defect: a guard
        // that counts rows without reading them).
        Assert.Equal("SKU-1", factPayload.Lines[0].ProductCode);
        Assert.Equal(3, factPayload.Lines[0].Units);
        Assert.Equal(2_000, factPayload.Lines[0].UnitPrice);
        Assert.Equal("SKU-2", factPayload.Lines[1].ProductCode);
        Assert.Equal(1, factPayload.Lines[1].Units);
        Assert.Equal(3_000, factPayload.Lines[1].UnitPrice);
        Assert.Equal(grossAmount, factPayload.Amount);
        Assert.Equal(discount, factPayload.Discount);
        Assert.Equal(totalAmount, factPayload.TotalAmount);
        Assert.Equal("CarrefourEs", factPayload.RetailerCode);
        Assert.Equal("IBERFOODS", factPayload.CompanyCode);

        // R40 — availableCredit numerically identical before and after,
        // recomputed from every credit_items row.
        var availableCreditAfter = await AvailableCreditAsync(creditId, connectionString);
        Assert.Equal(availableCreditBefore, availableCreditAfter);

        await host.StopAsync();
    }

    [Fact]
    public async Task BI9_ReturnsTheExistingReferenceWithCreatedFalse_WritesNoRowAndEmitsNoSecondFact_WhetherTheInvoiceIsIssuedOrPaid()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi9");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        // Issued repeat.
        const string issuedOrder = "ORD-000202";
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, issuedOrder, 4_000, "hold");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var firstCorrelationId = UniqueId.New();
        var firstRequest = BillingHostFixture.IssueRequest(issuedOrder, "CarrefourEs", "IBERFOODS", [("SKU-1", 2, 2_000)]);
        var firstReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(firstRequest), BuildHeaders(firstCorrelationId, UniqueId.New()));
        var firstPayload = RpcJson.Deserialize<InvoiceIssueReplyPayload>(firstReply.Data!);
        Assert.True(firstPayload.Created);

        var outboxCountBeforeRepeat = await WholeTableOutboxCountAsync(connectionString);

        var repeatReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(firstRequest), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var repeatPayload = RpcJson.Deserialize<InvoiceIssueReplyPayload>(repeatReply.Data!);

        Assert.False(repeatPayload.Created);
        Assert.Equal(firstPayload.InvoiceReference, repeatPayload.InvoiceReference);
        Assert.Equal(firstPayload.InvoiceDate, repeatPayload.InvoiceDate);
        Assert.Equal(firstPayload.Currency, repeatPayload.Currency);
        Assert.Equal(firstPayload.TotalAmount, repeatPayload.TotalAmount);
        Assert.Equal("issued", repeatPayload.Status);

        var invoiceRowsAfterRepeat = await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, issuedOrder);
        Assert.Single(invoiceRowsAfterRepeat);
        var consumeEntries = (await BillingHostFixture.LedgerOfAsync(mssql, connectionString, issuedOrder)).Where(e => e.Type == "consume").ToList();
        Assert.Single(consumeEntries);
        Assert.Equal(outboxCountBeforeRepeat, await WholeTableOutboxCountAsync(connectionString));

        // Paid repeat — feature 22 has no responder, so the paid invoice is
        // hand-seeded directly.
        const string paidOrder = "ORD-000203";
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, paidOrder, 5_000, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, paidOrder, 5_000, "consume");
        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-900001", paidOrder, "CarrefourEs", "IBERFOODS", 5_000, 0, 5_000, "paid", DateTime.UtcNow);

        var paidRequest = BillingHostFixture.IssueRequest(paidOrder, "CarrefourEs", "IBERFOODS", [("SKU-1", 5, 1_000)]);
        var paidReply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(paidRequest), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var paidPayload = RpcJson.Deserialize<InvoiceIssueReplyPayload>(paidReply.Data!);

        Assert.False(paidPayload.Created);
        Assert.Equal("paid", paidPayload.Status);
        Assert.Equal("INV-900001", paidPayload.InvoiceReference);

        await host.StopAsync();
    }

    [Fact]
    public async Task BI3_RepliesNotFoundNamingThePair_CreatesNoInvoice_AppendsNoLedgerEntry_AndEmitsNoFact_WhenNoCreditLineExists()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi3");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        const string orderReference = "ORD-000210";
        var request = BillingHostFixture.IssueRequest(orderReference, "NoSuchRetailer", "NoSuchCompany", [("SKU-1", 1, 1_000)]);

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("NoSuchRetailer", error.Details?["retailerCode"]?.ToString());
        Assert.Equal("NoSuchCompany", error.Details?["companyCode"]?.ToString());

        Assert.Empty(await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference));
        Assert.Empty(await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference));

        await host.StopAsync();
    }

    [Fact]
    public async Task BI4_RepliesValidationFailedCarryingTheExpectedAndReceivedCurrency_CreatesNoInvoice_AppendsNoLedgerEntry_AndEmitsNoFact()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi4");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000, "EUR");
        const string orderReference = "ORD-000211";
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, 1_000, "hold");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = new InvoiceIssueRequestPayload(orderReference, "CarrefourEs", "IBERFOODS", "GBP", [new Contracts.Facts.InvoiceLine("SKU-1", 1, 1_000)]);

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Equal("EUR", error.Details?["expected"]?.ToString());
        Assert.Equal("GBP", error.Details?["received"]?.ToString());

        Assert.Empty(await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference));

        await host.StopAsync();
    }

    [Fact]
    public async Task BI5_RepliesPreconditionFailedWithCodeNoActiveHold_CreatesNoInvoice_EmitsNoFact_AndLeavesTheOrdersExistingLedgerRowsUnchanged()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi5");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        const string orderReference = "ORD-000212";
        // A hold, then a release — the order's active hold is now zero.
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, 2_000, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, 2_000, "release");

        var ledgerBefore = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference);
        Assert.Equal(2, ledgerBefore.Count);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = BillingHostFixture.IssueRequest(orderReference, "CarrefourEs", "IBERFOODS", [("SKU-1", 1, 1_000)]);

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(request), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.Equal("NO_ACTIVE_HOLD", error.Details?["code"]?.ToString());

        Assert.Empty(await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference));

        // #7's N3 — re-read the order's existing ledger rows and confirm
        // the count is still 2, not merely that nothing new was ADDED.
        var ledgerAfter = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference);
        Assert.Equal(2, ledgerAfter.Count);

        await host.StopAsync();
    }

    /// <summary>
    /// `BI6` — one whole-table outbox row-count assertion after driving all
    /// four refusal paths in this file, so a fact leaking from ANY of them
    /// (not just the one under a scoped assertion) would be caught.
    /// </summary>
    [Fact]
    public async Task BI6_EmitsNoFactOfAnyTypeOnEveryRefusalPathOfBillingInvoiceIssue()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi6");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var outboxCountBefore = await WholeTableOutboxCountAsync(connectionString);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // BI2 — validation failure (empty lines).
        var invalidPayload = new InvoiceIssueRequestPayload("ORD-000220", "CarrefourEs", "IBERFOODS", "EUR", []);
        await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(invalidPayload), BuildHeaders(UniqueId.New(), UniqueId.New()));

        // BI3 — no credit line.
        var noCreditLinePayload = BillingHostFixture.IssueRequest("ORD-000221", "NoSuchRetailer", "NoSuchCompany", [("SKU-1", 1, 1_000)]);
        await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(noCreditLinePayload), BuildHeaders(UniqueId.New(), UniqueId.New()));

        // BI4 — currency mismatch. Needs an active hold to reach the check.
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, "ORD-000222", 1_000, "hold");
        var currencyMismatchPayload = new InvoiceIssueRequestPayload("ORD-000222", "CarrefourEs", "IBERFOODS", "GBP", [new Contracts.Facts.InvoiceLine("SKU-1", 1, 1_000)]);
        await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(currencyMismatchPayload), BuildHeaders(UniqueId.New(), UniqueId.New()));

        // BI5 — no active hold.
        var noHoldPayload = BillingHostFixture.IssueRequest("ORD-000223", "CarrefourEs", "IBERFOODS", [("SKU-1", 1, 1_000)]);
        await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(noHoldPayload), BuildHeaders(UniqueId.New(), UniqueId.New()));

        var outboxCountAfter = await WholeTableOutboxCountAsync(connectionString);
        Assert.Equal(outboxCountBefore, outboxCountAfter);

        await host.StopAsync();
    }

    /// <summary>
    /// `BI2`'s placement clause — observing the ENTRY, never the residue.
    /// This integration case's own comment states it does NOT prove
    /// placement: a rolled-back side effect is indistinguishable from one
    /// never attempted (#7's `N10`). The proof that the dispatcher is never
    /// called lives at the UNIT level (`InvoiceResponderValidationTests`).
    /// </summary>
    [Fact]
    public async Task BI2_AnswersValidationFailedAndWritesNothing_ForAnInvalidHeaderOrPayload()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-bi2");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        const string orderReference = "ORD-000230";
        var invalidPayload = new InvoiceIssueRequestPayload(orderReference, "CarrefourEs", "IBERFOODS", "EUR", []);

        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(invalidPayload), BuildHeaders(UniqueId.New(), UniqueId.New()));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);

        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Empty(await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference));
        Assert.Empty(await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference));
        Assert.Equal(0, await WholeTableOutboxCountAsync(connectionString));

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
