using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC25`, `BC3` on this subject — over the REAL responder, real MS-SQL, real NATS, real Kafka.</summary>
[Collection(BillingCollection.Name)]
public sealed class CreditReleaseTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task BC25_AppendsExactlyOneReleaseEntryAndEmitsExactlyOneCreditReleasedFactWithReasonOrderCancelled_ThenReportsReleasedFalseWritingNothingOnARepeat()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "release-bc25");
        using var _ = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, "ORD-000001", 5_000, "hold");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();

        var request = RpcJson.Serialize(new CreditReleaseRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS"));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditRelease, request, BuildHeaders(correlationId, requestId));

        var payload = RpcJson.Deserialize<CreditReleaseReplyPayload>(reply.Data!);
        Assert.True(payload.Released);
        Assert.Equal(5_000, payload.ReleasedAmount);
        Assert.Equal(500_000, payload.AvailableCreditAfter);

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000001");
        Assert.Equal(2, ledger.Count);
        var releaseEntry = Assert.Single(ledger, e => e.Type == "release");
        Assert.Equal(5_000, releaseEntry.Amount);

        var outboxRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.released.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factRow = Assert.Single(outboxRows);
        var factPayload = JsonSerializer.Deserialize<CreditReleasedPayload>(factRow.Payload, JsonWire.Options);
        Assert.Equal("order_cancelled", factPayload!.Reason);
        Assert.Equal(5_000, factPayload.ReleasedAmount);
        Assert.Equal(500_000, factPayload.AvailableCreditAfter);

        // BC28 — "every emitted fact", not only the approved one: the
        // released fact's own identity fields, D3.
        Assert.Equal("ORD-000001", factPayload.OrderReference);
        Assert.Equal("CarrefourEs", factPayload.RetailerCode);
        Assert.Equal("IBERFOODS", factPayload.CompanyCode);
        Assert.Equal("CR-000001", factPayload.CreditCode);
        Assert.Equal("EUR", factPayload.Currency);

        // Immediate repeat: released: false, no second entry, no second fact.
        var secondReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditRelease, request, BuildHeaders(correlationId, UniqueId.New()));
        var secondPayload = RpcJson.Deserialize<CreditReleaseReplyPayload>(secondReply.Data!);
        Assert.False(secondPayload.Released);

        var ledgerAfterRepeat = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, "ORD-000001");
        Assert.Equal(2, ledgerAfterRepeat.Count); // unchanged

        var outboxRowsAfterRepeat = await BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "credit.released.v1");
        Assert.Single(outboxRowsAfterRepeat); // no second fact

        await host.StopAsync();
    }

    [Fact]
    public async Task BC3_RepliesNotFoundOnThisSubject_WhenNoCreditLineExists()
    {
        var (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "release-bc3");
        using var _disposeHost = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = RpcJson.Serialize(new CreditReleaseRequestPayload("ORD-000099", "NoSuchRetailer", "NoSuchCompany"));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditRelease, request, BuildHeaders(UniqueId.New(), UniqueId.New()));

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("NOT_FOUND", error.Code);

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };
}
