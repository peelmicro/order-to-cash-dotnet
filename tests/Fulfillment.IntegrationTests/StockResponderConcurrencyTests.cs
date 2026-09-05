using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS18` — the held-lock proof of design.md §6.2, ledger L6, over the REAL responder.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockResponderConcurrencyTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task FS18_AnswersASecondRequestWhileAnEarlierOneIsBlockedOnAStockRowLockHeldByAnotherTransaction()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "concurrency-fs18");
        using var _ = host;

        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", "P1", units: 10);
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", "P2", units: 10);

        // The test itself holds an exclusive row lock on P1, outside the responder.
        await using var lockConnection = new SqlConnection(connectionString);
        await lockConnection.OpenAsync();
        var lockTransaction = lockConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = "SELECT id FROM dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE company_code = 'ACME' AND product_code = 'P1';";
            await lockCommand.ExecuteScalarAsync();
        }

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var requestP1 = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000800", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 2)]));
        var blockedTask = FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, requestP1, BuildHeaders(), TimeSpan.FromSeconds(30));

        // Give the blocked request a moment to actually reach the row lock.
        await Task.Delay(500);
        Assert.False(blockedTask.IsCompleted, "the P1 request should still be blocked on the held row lock.");

        // A second, INDEPENDENT request for P2 must be answered while the first is still outstanding.
        var requestP2 = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000801", "RETAILER1", "ACME", [new StockReserveRequestLine("P2", 2)]));
        var p2Reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, requestP2, BuildHeaders(), TimeSpan.FromSeconds(10));
        var p2Payload = RpcJson.Deserialize<StockReserveReplyPayload>(p2Reply.Data!);
        Assert.Equal("accepted", p2Payload.Outcome);

        Assert.False(blockedTask.IsCompleted, "the P1 request should STILL be blocked — the P2 answer must not have unblocked it.");

        // Release the held lock and confirm the first request now completes.
        await lockTransaction.CommitAsync();

        var p1Reply = await blockedTask;
        var p1Payload = RpcJson.Deserialize<StockReserveReplyPayload>(p1Reply.Data!);
        Assert.Equal("accepted", p1Payload.Outcome);

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders() => new()
    {
        { "x-correlation-id", UniqueId.New().Value.ToString() },
        { "x-request-id", UniqueId.New().Value.ToString() },
    };
}
