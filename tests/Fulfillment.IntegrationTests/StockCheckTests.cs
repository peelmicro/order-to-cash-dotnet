using System.Text.Json;
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

        // review D1 (round 2): this test's own name is a NEGATIVE — "never
        // with an RpcError" — so it must be asserted by READING the reply
        // body's shape, not left to an accidental null throw from
        // deserialising an error body straight into the typed success
        // payload's non-nullable `Lines`. Same discriminator shape as
        // RpcJson.IsErrorBody (Orders' NatsStockAvailabilityChecker /
        // NatsSagaCommandsAdapter, feature 46) — the RpcError schema's two
        // REQUIRED fields, code and message, together on no success reply.
        using (var document = JsonDocument.Parse(reply.Data!))
        {
            var root = document.RootElement;
            string? codeIfPresent = null;
            string? messageIfPresent = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var codeElement))
            {
                codeIfPresent = codeElement.GetString();
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var messageElement))
            {
                messageIfPresent = messageElement.GetString();
            }

            var looksLikeAnRpcError = codeIfPresent is not null && messageIfPresent is not null;
            Assert.False(
                looksLikeAnRpcError,
                $"fulfillment.stock.check answered an unknown product with an RpcError-shaped reply, not a StockCheckReplyPayload: code={codeIfPresent}, message={messageIfPresent}");
        }

        var payload = RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data!);
        Assert.False(payload.Available);
        var line = Assert.Single(payload.Lines);
        Assert.Equal(0, line.Available);
        Assert.False(line.Sufficient);

        await host.StopAsync();
    }
}
