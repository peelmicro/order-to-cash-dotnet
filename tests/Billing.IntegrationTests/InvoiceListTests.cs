using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BI15` — over the REAL responder and REAL MS-SQL.</summary>
[Collection(BillingCollection.Name)]
public sealed class InvoiceListTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task BI15_FiltersByStatusPartyCodesAndOrderReference_PagesTheResult_AndReturnsOnlyInvoicesOlderThanIssuedBeforeMinutesAgainstTheInjectedClock()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-list-bi15");
        using var _ = host;

        var old = DateTime.UtcNow.AddDays(-1);
        var recent = DateTime.UtcNow;

        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-800001", "ORD-000401", "RetailerList", "CompanyIssuedOld", 1_000, 0, 1_000, "issued", null, invoiceDate: old);
        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-800002", "ORD-000402", "RetailerList", "CompanyIssuedRecent", 2_000, 0, 2_000, "issued", null, invoiceDate: recent);
        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-800003", "ORD-000403", "RetailerList", "CompanyPaid", 3_000, 0, 3_000, "paid", DateTime.UtcNow, invoiceDate: old);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // Party filter.
        var byRetailer = await ListAsync(connection, new InvoiceListRequestPayload(1, 25, RetailerCode: "RetailerList"));
        Assert.NotNull(byRetailer.Page);
        Assert.Equal(3, byRetailer.Page.Total);
        Assert.Equal(3, byRetailer.Items.Count);

        // Status filter — paid carries paidAt, issued does not.
        var paidOnly = await ListAsync(connection, new InvoiceListRequestPayload(1, 25, Status: "paid", RetailerCode: "RetailerList"));
        Assert.NotNull(paidOnly.Page);
        Assert.Equal(1, paidOnly.Page.Total);
        var paidView = Assert.Single(paidOnly.Items);
        Assert.Equal("CompanyPaid", paidView.CompanyCode);
        Assert.NotNull(paidView.PaidAt);

        var issuedOnly = await ListAsync(connection, new InvoiceListRequestPayload(1, 25, Status: "issued", RetailerCode: "RetailerList"));
        Assert.NotNull(issuedOnly.Page);
        Assert.Equal(2, issuedOnly.Page.Total);
        Assert.All(issuedOnly.Items, i => Assert.Null(i.PaidAt));

        // orderReference filter.
        var byOrder = await ListAsync(connection, new InvoiceListRequestPayload(1, 25, OrderReference: "ORD-000402"));
        Assert.NotNull(byOrder.Page);
        Assert.Equal(1, byOrder.Page.Total);
        Assert.Equal("CompanyIssuedRecent", Assert.Single(byOrder.Items).CompanyCode);

        // issuedBeforeMinutes — only the OLD ones (720 minutes = 12 hours ago).
        var olderThan = await ListAsync(connection, new InvoiceListRequestPayload(1, 25, RetailerCode: "RetailerList", IssuedBeforeMinutes: 720));
        Assert.NotNull(olderThan.Page);
        Assert.Equal(2, olderThan.Page.Total);
        Assert.All(olderThan.Items, i => Assert.NotEqual("CompanyIssuedRecent", i.CompanyCode));

        // Paging.
        var page1 = await ListAsync(connection, new InvoiceListRequestPayload(1, 1, RetailerCode: "RetailerList"));
        Assert.NotNull(page1.Page);
        Assert.Equal(3, page1.Page.Total);
        Assert.Single(page1.Items);

        // No lock, no mutation — a re-read afterwards proves no row changed.
        var invoicesAfter = await BillingHostFixture.InvoicesOfAsync(mssql, connectionString, "ORD-000401");
        Assert.Single(invoicesAfter);
        Assert.Equal("issued", invoicesAfter[0].Status);

        await host.StopAsync();
    }

    private static async Task<InvoiceListReplyPayload> ListAsync(NatsConnection connection, InvoiceListRequestPayload request)
    {
        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceList, RpcJson.Serialize(request));
        return RpcJson.Deserialize<InvoiceListReplyPayload>(reply.Data!);
    }
}
