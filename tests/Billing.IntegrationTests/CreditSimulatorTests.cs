using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Contracts.Wire;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Feature 20 — R42, R43, R44, over the REAL responder, real MS-SQL, real
/// NATS, real Kafka, with the REAL <c>SimulatorCreditDecision</c> bound
/// (the default registration since this feature landed — no
/// <c>decisionPort</c> override, unlike <c>CreditHoldTests</c>'s
/// <c>AlwaysRefuseCreditDecision</c> probe, which exercised only the
/// wiring, never the simulator's own rules).
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class CreditSimulatorTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task R42_ATotalEndingIn99IsRejectedWithSimulatedCentsRule_EvenWithAmpleCredit_OverTheRealWire()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "simulator-r42");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000101", "CarrefourEs", "IBERFOODS", new CreditMoney(24_999, "EUR"))); // cents-rule-intentional
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("rejected", payload.Outcome);
        Assert.Equal("simulated_cents_rule", payload.Reason);
        Assert.Equal(500_000, payload.AvailableCredit); // UNCHANGED — ample credit, refused regardless

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000101");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factPayload = JsonSerializer.Deserialize<CreditRejectedPayload>(Assert.Single(outboxRows).Payload, JsonWire.Options);
        Assert.Equal("simulated_cents_rule", factPayload!.Reason);
        Assert.Equal(24_999, factPayload.RequestedAmount); // cents-rule-intentional
        Assert.Equal(500_000, factPayload.AvailableCredit);

        await host.StopAsync();
    }

    [Fact]
    public async Task R43_ANonCentsAmountIsRejectedWithSimulatedFailureRate_WhenTheConfiguredRateIsOne_OverTheRealWire()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(
            mssql, nats, kafka, "simulator-r43",
            configure: options => options.CreditFailureRate = 1);
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        // Deliberately NOT ending in 99, so this is the failure-rate branch
        // and not R42's cents rule.
        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000102", "CarrefourEs", "IBERFOODS", new CreditMoney(25_000, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("rejected", payload.Outcome);
        Assert.Equal("simulated_failure_rate", payload.Reason);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000102");
        Assert.Empty(ledger);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factPayload = JsonSerializer.Deserialize<CreditRejectedPayload>(Assert.Single(outboxRows).Payload, JsonWire.Options);
        Assert.Equal("simulated_failure_rate", factPayload!.Reason);
        Assert.Equal(25_000, factPayload.RequestedAmount);

        await host.StopAsync();
    }

    /// <summary>
    /// R43 default: with NO <c>CREDIT_FAILURE_RATE</c> override — the same
    /// default <c>BillingHostFixture.StartHostAsync</c> every other Billing
    /// integration test uses — a fitting, non-`.99` hold is APPROVED. This
    /// is what makes every pre-existing Billing integration fixture safe:
    /// none of them is `≡ 99 (mod 100)` (verified by inspection, recorded
    /// in `progress/impl_billing_credit_simulator.md`), so none of them can
    /// be silently rejected now that the simulator is the default binding.
    /// </summary>
    [Fact]
    public async Task R43_DefaultsToZero_SoAFittingNonCentsHoldIsApproved_OverTheRealWire()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "simulator-r43-default");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var headers = BuildHeaders(UniqueId.New(), UniqueId.New());

        var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000103", "CarrefourEs", "IBERFOODS", new CreditMoney(25_000, "EUR")));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

        var payload = RpcJson.Deserialize<CreditHoldReplyPayload>(reply.Data!);
        Assert.Equal("approved", payload.Outcome);

        await host.StopAsync();
    }

    /// <summary>
    /// R44, field by field, comparing the ACTUAL keys of both wire payloads
    /// to EACH OTHER — never to a hand-typed literal list (the exact
    /// weakness #7's reviewer found in its own R44 parity spec, N1 in
    /// `review_billing_credit_simulator.md`: a literal key list is a
    /// tautology once written, and stops catching a payload that grows a
    /// ninth key on one path only). A genuine `over_limit` rejection stays
    /// reachable WITH the simulator bound at its default rate (R44's own
    /// "not bypass R37" clause) in the same run.
    /// </summary>
    [Fact]
    public async Task R44_SimulatedAndGenuineRejectionsShareTheSameFactTypeAndPayloadKeySet_DifferingOnlyInReason()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "simulator-r44");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000002", "CarrefourEs", "OVERLIMITCO", 10_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // The simulated rejection — a `.99` amount, ample credit.
        var simulatedCorrelationId = UniqueId.New();
        var simulatedRequest = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000104", "CarrefourEs", "IBERFOODS", new CreditMoney(24_999, "EUR"))); // cents-rule-intentional
        var simulatedReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, simulatedRequest, BuildHeaders(simulatedCorrelationId, UniqueId.New()));
        var simulatedPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(simulatedReply.Data!);
        Assert.Equal("simulated_cents_rule", simulatedPayload.Reason);

        // The genuine rejection — over the retailer's limit, never touches the port (BC13).
        var overLimitCorrelationId = UniqueId.New();
        var overLimitRequest = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000105", "CarrefourEs", "OVERLIMITCO", new CreditMoney(20_000, "EUR")));
        var overLimitReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, overLimitRequest, BuildHeaders(overLimitCorrelationId, UniqueId.New()));
        var overLimitPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(overLimitReply.Data!);
        Assert.Equal("over_limit", overLimitPayload.Reason);

        var simulatedOutboxRow = Assert.Single(await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, simulatedCorrelationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10)));
        var overLimitOutboxRow = Assert.Single(await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, overLimitCorrelationId.Value, "credit.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10)));

        Assert.Equal("credit.rejected.v1", simulatedOutboxRow.EventType);
        Assert.Equal("credit.rejected.v1", overLimitOutboxRow.EventType);

        var simulatedKeys = JsonDocument.Parse(simulatedOutboxRow.Payload).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var overLimitKeys = JsonDocument.Parse(overLimitOutboxRow.Payload).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(simulatedKeys, overLimitKeys);

        var simulatedFact = JsonSerializer.Deserialize<CreditRejectedPayload>(simulatedOutboxRow.Payload, JsonWire.Options)!;
        var overLimitFact = JsonSerializer.Deserialize<CreditRejectedPayload>(overLimitOutboxRow.Payload, JsonWire.Options)!;

        // Normalise the two fields that legitimately differ by construction
        // (`Reason`, and the different order/retailer identity each
        // fixture used) — everything else about the SHAPE must match.
        Assert.Equal("simulated_cents_rule", simulatedFact.Reason);
        Assert.Equal("over_limit", overLimitFact.Reason);
        Assert.Equal(simulatedFact.RetailerCode, overLimitFact.RetailerCode);
        Assert.Equal(simulatedFact.Currency, overLimitFact.Currency);

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };
}
