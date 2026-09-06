using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC21` — the held-lock proof of design.md §4.1, ledger `L21`, over the REAL responder.</summary>
[Collection(BillingCollection.Name)]
public sealed class CreditResponderConcurrencyTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task BC21_AnswersASecondRequestWhileAnEarlierOneIsBlockedOnACreditRowLockHeldByAnotherTransaction()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "concurrency-bc21");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000A", "RetailerA", "CompanyA", 10_000);
        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000B", "RetailerB", "CompanyB", 10_000);

        // The test itself holds an exclusive row lock on line A, outside the responder.
        await using var lockConnection = new SqlConnection(connectionString);
        await lockConnection.OpenAsync();
        var lockTransaction = lockConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
        await using (var lockCommand = lockConnection.CreateCommand())
        {
            lockCommand.Transaction = lockTransaction;
            lockCommand.CommandText = "SELECT id FROM dbo.credits WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE retailer_code = 'RetailerA' AND company_code = 'CompanyA';";
            await lockCommand.ExecuteScalarAsync();
        }

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var requestA = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000900", "RetailerA", "CompanyA", new CreditMoney(1_000, "EUR")));
        var blockedTask = BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, requestA, BuildHeaders(), TimeSpan.FromSeconds(30));

        // Give the blocked request a moment to actually reach the row lock.
        await Task.Delay(500);
        Assert.False(blockedTask.IsCompleted, "the line A request should still be blocked on the held row lock.");

        // A second, INDEPENDENT request for line B must be answered while the first is still outstanding.
        var requestB = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000901", "RetailerB", "CompanyB", new CreditMoney(1_000, "EUR")));
        var bReply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, requestB, BuildHeaders(), TimeSpan.FromSeconds(10));
        var bPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(bReply.Data!);
        Assert.Equal("approved", bPayload.Outcome);

        Assert.False(blockedTask.IsCompleted, "the line A request should STILL be blocked — the line B answer must not have unblocked it.");

        // Release the held lock and confirm the first request now completes.
        await lockTransaction.CommitAsync();

        var aReply = await blockedTask;
        var aPayload = RpcJson.Deserialize<CreditHoldReplyPayload>(aReply.Data!);
        Assert.Equal("approved", aPayload.Outcome);

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders() => new()
    {
        { "x-correlation-id", UniqueId.New().Value.ToString() },
        { "x-request-id", UniqueId.New().Value.ToString() },
    };
}
