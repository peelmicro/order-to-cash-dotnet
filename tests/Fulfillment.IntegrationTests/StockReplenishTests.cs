using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS14`, and the happy path — `R61`'s "no fact", observable end to end.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockReplenishTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task FS14_RepliesNotFoundAndReplenishesNoLine_WhenAnyLineNamesAnUnknownProduct()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "replenish-fs14");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var request = RpcJson.Serialize(new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("P1", 5), new StockReplenishRequestLine("UNKNOWN", 3)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReplenish, request);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("NOT_FOUND", error.Code);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(10, row!.Units); // untouched — all-or-nothing

        await host.StopAsync();
    }

    [Fact]
    public async Task HappyPath_UnitsUp_ReservedUnitsAndReservationsUntouched_OutboxEmpty()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "replenish-happy");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 4);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000020", 4, "reserved");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var request = RpcJson.Serialize(new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("P1", 20)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReplenish, request);

        var payload = RpcJson.Deserialize<StockReplenishReplyPayload>(reply.Data!);
        var item = Assert.Single(payload.Items);
        Assert.Equal(30, item.Units);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(30, row!.Units);
        Assert.Equal(4, row.ReservedUnits);

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000020");
        Assert.Equal("reserved", Assert.Single(reservations).Status);

        await using var db = mssql.CreateDbContext(connectionString);
        Assert.Equal(0, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.OutboxMessages));

        await host.StopAsync();
    }
}
