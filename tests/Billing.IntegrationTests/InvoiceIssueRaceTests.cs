using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// `BI8` — two concurrent `billing.invoice.issue` requests for the SAME
/// order produce exactly one invoice, one `consume` entry and one
/// `invoice.issued.v1`, with no deadlock and no lock-wait timeout. Repeated
/// on ten fresh orders so a scheduling fluke is visible rather than lucky.
/// </summary>
/// <remarks>
/// This file CANNOT detect a lock-order inversion: two instances of the
/// same transaction taking locks in a consistently inverted order still
/// cannot cycle — established empirically by #7's reviewer (`N5`). The
/// ordered three-lock call log in <c>InvoiceIssueServiceTests</c> (`D3`) is
/// the SOLE guard for `BI8`'s ordering clause.
/// </remarks>
[Collection(BillingCollection.Name)]
public sealed class InvoiceIssueRaceTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task BI8_TwoConcurrentIssueRequestsForOneOrderProduceExactlyOneInvoiceOneConsumeEntryAndOneInvoiceIssuedFact_WithNoDeadlock()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-race-bi8");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var code = $"CR-INVRACE-{i:D4}";
            var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, code, "CarrefourEs", $"INVRACE{i}", 50_000);
            var orderReference = $"ORD-{900000 + i}";
            await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, 5_000, "hold");

            var request = BillingHostFixture.IssueRequest(orderReference, "CarrefourEs", $"INVRACE{i}", [("SKU-1", 5, 1_000)]);
            var requestBytes = RpcJson.Serialize(request);

            await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });

            var correlationIdA = UniqueId.New();
            var correlationIdB = UniqueId.New();

            var taskA = BillingHostFixture.RequestBareAsync(connectionA, InvoiceSubjects.InvoiceIssue, requestBytes, BuildHeaders(correlationIdA), TimeSpan.FromSeconds(15));
            var taskB = BillingHostFixture.RequestBareAsync(connectionB, InvoiceSubjects.InvoiceIssue, requestBytes, BuildHeaders(correlationIdB), TimeSpan.FromSeconds(15));

            var results = await Task.WhenAll(taskA, taskB);

            var payloadA = RpcJson.Deserialize<InvoiceIssueReplyPayload>(results[0].Data!);
            var payloadB = RpcJson.Deserialize<InvoiceIssueReplyPayload>(results[1].Data!);

            var createdFlags = new[] { payloadA.Created, payloadB.Created };
            Assert.Contains(true, createdFlags);
            Assert.Contains(false, createdFlags);
            Assert.Equal(payloadA.InvoiceReference, payloadB.InvoiceReference);

            var invoiceRows = await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, orderReference);
            Assert.Single(invoiceRows);

            var consumeEntries = (await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference)).Where(e => e.Type == "consume").ToList();
            Assert.Single(consumeEntries);

            var winningCorrelationId = payloadA.Created ? correlationIdA : correlationIdB;
            var factRows = await BillingHostFixture.WaitForAsync(
                () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, winningCorrelationId.Value, "invoice.issued.v1"),
                rows => rows.Count > 0,
                TimeSpan.FromSeconds(10));
            Assert.Single(factRows);
        }

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", UniqueId.New().Value.ToString() },
    };
}
