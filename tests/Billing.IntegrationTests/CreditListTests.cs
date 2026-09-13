using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC6` — over the REAL responder and REAL MS-SQL, at SQL level.</summary>
[Collection(BillingCollection.Name)]
public sealed class CreditListTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task BC6_ReportsActiveHoldsOpenExposureAndAvailableCreditThatReconcileToTheCreditLimitForEveryListedLine()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "list-bc6");
        using var _ = host;

        // Shape 1: never held.
        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-LIST-01", "RetailerList", "CompanyNeverHeld", 10_000);

        // Shape 2: held only.
        var heldId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-LIST-02", "RetailerList", "CompanyHeld", 10_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, heldId, "ORD-LIST-002", 2_000, "hold");

        // Shape 3: held + consumed.
        var consumedId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-LIST-03", "RetailerList", "CompanyConsumed", 10_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, consumedId, "ORD-LIST-003", 3_000, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, consumedId, "ORD-LIST-003", 3_000, "consume");

        // Shape 4: held + released.
        var releasedId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-LIST-04", "RetailerList", "CompanyReleased", 10_000);
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, releasedId, "ORD-LIST-004", 4_000, "hold");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, releasedId, "ORD-LIST-004", 4_000, "release");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var reply = await ListAsync(connection, new CreditListRequestPayload(1, 25, RetailerCode: "RetailerList"));
        // BI30/backlog id 55 — assert the reply's OWN discriminating field
        // before touching a collection: an RpcError body deserialises into
        // an all-defaults reply record without throwing, so a bare
        // `.Items.Count` assertion would pass silently on a refusal.
        Assert.NotNull(reply.Page);
        Assert.Equal(4, reply.Page.Total);
        Assert.Equal(4, reply.Items.Count);

        foreach (var item in reply.Items)
        {
            // BC6's own reconciliation identity: activeHolds + openExposure = creditLimit − availableCredit.
            Assert.Equal(item.CreditLimit - item.AvailableCredit, item.ActiveHolds + item.OpenExposure);

            switch (item.CompanyCode)
            {
                case "CompanyNeverHeld":
                    Assert.Equal(0, item.ActiveHolds);
                    Assert.Equal(0, item.OpenExposure);
                    Assert.Equal(10_000, item.AvailableCredit);
                    break;
                case "CompanyHeld":
                    Assert.Equal(2_000, item.ActiveHolds);
                    Assert.Equal(0, item.OpenExposure);
                    Assert.Equal(8_000, item.AvailableCredit);
                    break;
                case "CompanyConsumed":
                    Assert.Equal(0, item.ActiveHolds);
                    Assert.Equal(3_000, item.OpenExposure);
                    Assert.Equal(7_000, item.AvailableCredit); // consume is numerically neutral (R40)
                    break;
                case "CompanyReleased":
                    Assert.Equal(0, item.ActiveHolds);
                    Assert.Equal(0, item.OpenExposure);
                    Assert.Equal(10_000, item.AvailableCredit); // fully restored
                    break;
            }
        }

        // Filters and paging at SQL level.
        var filteredByCompany = await ListAsync(connection, new CreditListRequestPayload(1, 25, RetailerCode: "RetailerList", CompanyCode: "CompanyHeld"));
        Assert.NotNull(filteredByCompany.Page);
        Assert.Equal(1, filteredByCompany.Page.Total);
        var single = Assert.Single(filteredByCompany.Items);
        Assert.Equal("CompanyHeld", single.CompanyCode);

        var page1 = await ListAsync(connection, new CreditListRequestPayload(1, 1, RetailerCode: "RetailerList"));
        Assert.NotNull(page1.Page);
        Assert.Equal(4, page1.Page.Total);
        Assert.Single(page1.Items);

        await host.StopAsync();
    }

    private static async Task<CreditListReplyPayload> ListAsync(NatsConnection connection, CreditListRequestPayload request)
    {
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditList, RpcJson.Serialize(request));
        return RpcJson.Deserialize<CreditListReplyPayload>(reply.Data!);
    }
}
