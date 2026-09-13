using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Contracts.Wire;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// `R38`/`R39` integration halves, `BC1`, `BC3`, `BC4`, `BC7`, `BC8`,
/// `BC28` — over the REAL responder, real MS-SQL, real NATS, real Kafka.
/// Every reply assertion opens the body and asserts its own discriminating
/// field before touching a collection (backlog id 53's shape, `BC32`).
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class CreditHoldTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task R38_ApprovedPath_OneHoldRowAndOneCreditApprovedV1CarryingTheHeldAmountAndTheResultingAvailableCredit()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-r38");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();
        var headers = BuildHeaders(correlationId, requestId);

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("approved", payload.Outcome);
        Assert.Equal(1_000, payload.HeldAmount);
        Assert.Equal(499_000, payload.AvailableCredit);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000001");
        var entry = Assert.Single(ledger);
        Assert.Equal("hold", entry.Type);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC1_StampsCorrelationIdFromTheHeaderAndCausationIdFromTheRequestId_OnTheEmittedCreditApprovedFact()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc1");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();
        var headers = BuildHeaders(correlationId, requestId);

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000002", "CarrefourEs", "IBERFOODS", new CreditMoney(2_000, "EUR")));
        await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.approved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var row = Assert.Single(outboxRows);
        Assert.Equal(correlationId.Value, row.CorrelationId);
        Assert.Equal(requestId.Value, row.CausationId);

        var factPayload = JsonSerializer.Deserialize<CreditApprovedPayload>(row.Payload, JsonWire.Options);
        Assert.Equal(498_000, factPayload!.AvailableCreditAfter);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC28_EmitsTheResolvedCreditLinesCodesOnTheFact_NotTheRequestsEchoedStrings()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc28");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        // Differently-cased retailerCode/companyCode from what was seeded —
        // the fact must carry the STORED casing, not the request's echo.
        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000003", "CARREFOURES", "iberfoods", new CreditMoney(500, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);
        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("approved", payload.Outcome);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.approved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factPayload = JsonSerializer.Deserialize<CreditApprovedPayload>(Assert.Single(outboxRows).Payload, JsonWire.Options);
        Assert.Equal("CarrefourEs", factPayload!.RetailerCode);
        Assert.Equal("IBERFOODS", factPayload.CompanyCode);
        Assert.Equal("CR-000001", factPayload.CreditCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task R39_OverLimit_RejectedReplyWithNoLedgerEntryAndOneCreditRejectedV1NamingReasonRequestedAmountAndAvailableCredit()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-r39-overlimit");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 1_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000004", "CarrefourEs", "IBERFOODS", new CreditMoney(1_500, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("rejected", payload.Outcome);
        Assert.Equal("over_limit", payload.Reason);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000004");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factPayload = JsonSerializer.Deserialize<CreditRejectedPayload>(Assert.Single(outboxRows).Payload, JsonWire.Options);
        Assert.Equal("over_limit", factPayload!.Reason);
        Assert.Equal(1_500, factPayload.RequestedAmount);
        Assert.Equal(1_000, factPayload.AvailableCredit);

        // BC28 — "every emitted fact", not only the approved one: the
        // rejected fact's own identity fields, D3.
        Assert.Equal("ORD-000004", factPayload.OrderReference);
        Assert.Equal("CarrefourEs", factPayload.RetailerCode);
        Assert.Equal("IBERFOODS", factPayload.CompanyCode);
        Assert.Equal("CR-000001", factPayload.CreditCode);
        Assert.Equal("EUR", factPayload.Currency);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC3_RepliesNotFoundNamingThePair_WritingNoLedgerEntryAndEmittingNoFact_WhenNoCreditLineExists()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc3");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000005", "NoSuchRetailer", "NoSuchCompany", new CreditMoney(500, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal("NoSuchRetailer", error.Details!["retailerCode"]!.ToString());
        Assert.Equal("NoSuchCompany", error.Details!["companyCode"]!.ToString());

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000005");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(outboxRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC4_RepliesValidationFailedWritingNoLedgerEntryAndEmittingNoFact_WhenTheRequestedCurrencyDiffersFromTheLines()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc4");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000, "EUR");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000006", "CarrefourEs", "IBERFOODS", new CreditMoney(500, "GBP")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Equal("EUR", error.Details!["expected"]!.ToString());
        Assert.Equal("GBP", error.Details!["received"]!.ToString());

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000006");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(outboxRows);

        await host.StopAsync();
    }

    /// <summary>
    /// The level #7 could not reach (design.md §6.4, `D1`): a REFUSING port
    /// bound at the real host, over the real wire — the exact seam #7's
    /// integration harness bound `AlwaysApproveCreditDecision`
    /// UNCONDITIONALLY and therefore could never exercise.
    /// </summary>
    [Fact]
    public async Task ARefusingPort_ProducesTheRejectedReplyAndFact_OverTheRealWire()
    {
        var refusingPort = new AlwaysRefuseCreditDecision(AdapterRejectionReason.SimulatedCentsRule);
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-refusing-port", decisionPort: refusingPort);
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000007", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("rejected", payload.Outcome);
        Assert.Equal("simulated_cents_rule", payload.Reason);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000007");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factPayload = JsonSerializer.Deserialize<CreditRejectedPayload>(Assert.Single(outboxRows).Payload, JsonWire.Options);
        Assert.Equal("simulated_cents_rule", factPayload!.Reason);
        Assert.Equal(1_000, factPayload.RequestedAmount);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC7_AnswersAlreadyHeldWithTheRecordedHeldAmountAndTheCurrentAvailableCredit_WritingNoEntryAndEmittingNoSecondFact_WhenReIssued()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc7-reissue");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000008", "CarrefourEs", "IBERFOODS", new CreditMoney(3_000, "EUR")));
        var firstReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, BuildHeaders(correlationId, UniqueId.New()));
        var firstPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(firstReply.Data!);
        Assert.Equal("approved", firstPayload.Outcome);

        var secondReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, BuildHeaders(correlationId, UniqueId.New()));
        var secondPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(secondReply.Data!);
        Assert.Equal("already_held", secondPayload.Outcome);
        Assert.Equal(3_000, secondPayload.HeldAmount);
        Assert.Equal(497_000, secondPayload.AvailableCredit);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000008");
        Assert.Single(ledger); // unchanged by the re-issue

        var factRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.approved.v1");
        Assert.Single(factRows); // no second fact

        await host.StopAsync();
    }

    /// <summary>`BC7`'s released-then-re-issued variant: a hold that was later released still short-circuits to `already_held`, reporting the RECORDED heldAmount and the CURRENT availableCredit — the two legitimately differ here.</summary>
    [Fact]
    public async Task BC7_ReIssuedAfterRelease_AnswersAlreadyHeldWithTheRecordedAmountAndTheCurrentAvailableCredit()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc7-released");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, "ORD-000009", 4_000, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, "ORD-000009", 4_000, "release");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000009", "CarrefourEs", "IBERFOODS", new CreditMoney(4_000, "EUR")));

        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, BuildHeaders(correlationId, UniqueId.New()));
        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);

        Assert.Equal("already_held", payload.Outcome);
        Assert.Equal(4_000, payload.HeldAmount); // the RECORDED hold amount
        Assert.Equal(500_000, payload.AvailableCredit); // the CURRENT available credit — fully restored

        var ledgerCountBefore = (await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000009")).Count;
        Assert.Equal(2, ledgerCountBefore); // unchanged

        var factRows = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task BC8_ReEvaluatesAPreviouslyRejectedHoldFromScratch_AndWritesNoLedgerEntryEitherTime()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "hold-bc8-rejected-reissue");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 1_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000010", "CarrefourEs", "IBERFOODS", new CreditMoney(1_500, "EUR")));

        var firstReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, BuildHeaders(UniqueId.New(), UniqueId.New()));
        var firstPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(firstReply.Data!);
        Assert.Equal("rejected", firstPayload.Outcome);

        var secondReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, BuildHeaders(UniqueId.New(), UniqueId.New()));
        var secondPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(secondReply.Data!);
        Assert.Equal("rejected", secondPayload.Outcome); // re-evaluated from scratch, not remembered

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000010");
        Assert.Empty(ledger); // neither time

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };

    private sealed class AlwaysRefuseCreditDecision(AdapterRejectionReason reason) : ICreditDecisionPort
    {
        public ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CreditDecision>(new CreditDecision.Refuse(reason));
    }
}
