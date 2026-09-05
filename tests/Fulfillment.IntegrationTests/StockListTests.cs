using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS15` — over the REAL responder and REAL MS-SQL.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockListTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task FS15_ListsStockViewsWithDerivedAvailableUnits_Pages_FiltersByCompanyAndProduct_AndReturnsOnlyBelowThresholdItemsWhenAsked_WithoutLockingOrMutating()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "list-fs15");
        using var _ = host;

        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", "P1", units: 10, reservedUnits: 2, lowStockThreshold: 5); // available 8, above threshold
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", "P2", units: 10, reservedUnits: 8, lowStockThreshold: 5); // available 2, BELOW threshold
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "OTHERCO", "P1", units: 10, reservedUnits: 0, lowStockThreshold: 5);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // Filter by companyCode.
        var byCompany = await ListAsync(connection, new StockListRequestPayload(null, null, CompanyCode: "ACME"));
        Assert.Equal(2, byCompany.Items.Count);
        Assert.All(byCompany.Items, item => Assert.Equal("ACME", item.CompanyCode));

        // Filter by productCode.
        var byProduct = await ListAsync(connection, new StockListRequestPayload(null, null, ProductCode: "P1"));
        Assert.Equal(2, byProduct.Items.Count);
        Assert.All(byProduct.Items, item => Assert.Equal("P1", item.ProductCode));

        // belowThreshold: only the short item.
        var belowThreshold = await ListAsync(connection, new StockListRequestPayload(null, null, CompanyCode: "ACME", BelowThreshold: true));
        var single = Assert.Single(belowThreshold.Items);
        Assert.Equal("P2", single.ProductCode);
        Assert.Equal(2, single.AvailableUnits);

        // Paging.
        var page1 = await ListAsync(connection, new StockListRequestPayload(1, 1));
        Assert.Single(page1.Items);
        Assert.Equal(3, page1.Page.Total);

        // No mutation, no lock: re-read directly proves the rows are untouched.
        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(10, row!.Units);

        await host.StopAsync();
    }

    private static async Task<StockListReplyPayload> ListAsync(NatsConnection connection, StockListRequestPayload request)
    {
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockList, RpcJson.Serialize(request));
        return RpcJson.Deserialize<StockListReplyPayload>(reply.Data!);
    }
}
