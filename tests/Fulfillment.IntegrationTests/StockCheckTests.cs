using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary><c>R31</c>, `FS22` — over the REAL responder, real MS-SQL, real NATS.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockCheckTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task R31_AnswersPerLineWithoutMutatingAStockItemAndWithoutEmittingAFact()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "check-r31");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 3);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var request = RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 4)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockCheck, request);

        var payload = RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data!);
        Assert.True(payload.Available);
        var line = Assert.Single(payload.Lines);
        Assert.Equal(7, line.Available); // 10 - 3
        Assert.True(line.Sufficient);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(10, row!.Units);
        Assert.Equal(3, row.ReservedUnits);

        await using var readDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(0, await readDb.OutboxMessages.CountAsync());

        await host.StopAsync();
    }

    [Fact]
    public async Task FS22_AnswersAnUnknownProductWithAvailableZeroAndSufficientFalse_NeverWithAnRpcError()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "check-fs22");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var request = RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("UNKNOWN", 1)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockCheck, request);

        var payload = RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data!);
        Assert.False(payload.Available);
        var line = Assert.Single(payload.Lines);
        Assert.Equal(0, line.Available);
        Assert.False(line.Sufficient);

        await host.StopAsync();
    }
}
