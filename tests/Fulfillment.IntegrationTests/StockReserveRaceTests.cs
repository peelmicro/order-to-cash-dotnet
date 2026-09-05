using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS6`, `FS7`, `FS19`'s deadlock-shape half — the check-then-reserve race, honestly (design.md §4).</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockReserveRaceTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    /// <summary>
    /// D1's second half (review §12.2): the reply assertions alone do not
    /// observe the two facts the transaction is supposed to have written.
    /// `tasks.md` G5 and `design.md` §14 both require asserting the outbox
    /// directly — exactly one `stock.reserved.v1` and exactly one
    /// `stock.rejected.v1` per iteration, keyed by each order's own
    /// correlation id.
    /// </summary>
    [Fact]
    public async Task FS6_TwoConcurrentReservesForTheLastUnits_YieldExactlyOneStockReservedAndOneStockRejected_AndReservedUnitsNeverExceedsUnits()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "race-fs6");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var product = $"RACE-{i}";
            var stockId = Guid.NewGuid();
            await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", product, units: 5);

            var orderA = $"ORD-{600000 + (i * 2)}";
            var orderB = $"ORD-{600000 + (i * 2) + 1}";
            var correlationIdA = UniqueId.New();
            var correlationIdB = UniqueId.New();

            await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });

            var requestA = RpcJson.Serialize(new StockReserveRequestPayload(orderA, "RETAILER1", "ACME", [new StockReserveRequestLine(product, 5)]));
            var requestB = RpcJson.Serialize(new StockReserveRequestPayload(orderB, "RETAILER1", "ACME", [new StockReserveRequestLine(product, 5)]));

            var taskA = FulfillmentHostFixture.RequestBareAsync(connectionA, StockSubjects.StockReserve, requestA, BuildHeaders(correlationIdA));
            var taskB = FulfillmentHostFixture.RequestBareAsync(connectionB, StockSubjects.StockReserve, requestB, BuildHeaders(correlationIdB));

            var results = await Task.WhenAll(taskA, taskB);

            var payloadA = RpcJson.Deserialize<StockReserveReplyPayload>(results[0].Data!);
            var payloadB = RpcJson.Deserialize<StockReserveReplyPayload>(results[1].Data!);

            var outcomes = new[] { payloadA.Outcome, payloadB.Outcome };
            Assert.Contains("accepted", outcomes);
            Assert.Contains("rejected", outcomes);

            var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", product);
            Assert.Equal(5, row!.ReservedUnits); // exactly the winner's units — never exceeds Units (5)
            Assert.True(row.ReservedUnits <= row.Units);

            // D1: the outbox is the only durable record of which order won —
            // assert both facts directly rather than trusting the replies.
            var winningCorrelationId = payloadA.Outcome == "accepted" ? correlationIdA : correlationIdB;
            var losingCorrelationId = payloadA.Outcome == "accepted" ? correlationIdB : correlationIdA;

            var reservedRows = await FulfillmentHostFixture.WaitForAsync(
                () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, winningCorrelationId.Value, "stock.reserved.v1"),
                rows => rows.Count > 0,
                TimeSpan.FromSeconds(10));
            Assert.Single(reservedRows);

            var rejectedRows = await FulfillmentHostFixture.WaitForAsync(
                () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, losingCorrelationId.Value, "stock.rejected.v1"),
                rows => rows.Count > 0,
                TimeSpan.FromSeconds(10));
            Assert.Single(rejectedRows);
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task FS7_ALineReportedSufficientByStockCheck_IsRejectedByALaterReserveOnceAnotherOrderTookTheUnits()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "race-fs7");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 5);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // The check reports sufficient — a non-locking read that holds nothing.
        var checkRequest = RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 5)]));
        var checkReply = RpcJson.Deserialize<StockCheckReplyPayload>((await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockCheck, checkRequest)).Data!);
        Assert.True(checkReply.Available);

        // Another order takes every unit.
        var firstReserve = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000700", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 5)]));
        var firstReply = RpcJson.Deserialize<StockReserveReplyPayload>((await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, firstReserve, BuildHeaders())).Data!);
        Assert.Equal("accepted", firstReply.Outcome);

        // The later reserve, for the SAME line the check reported sufficient, is rejected cleanly.
        var secondReserve = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000701", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 5)]));
        var secondReply = RpcJson.Deserialize<StockReserveReplyPayload>((await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, secondReserve, BuildHeaders())).Data!);
        Assert.Equal("rejected", secondReply.Outcome);

        await host.StopAsync();
    }

    /// <summary>
    /// `FS19`'s deadlock-shape half, ledger L2 — the application-fixed
    /// per-row lock order is what makes this safe: MS-SQL gives no guarantee
    /// about a multi-row seek's lock-acquisition order.
    /// </summary>
    [Fact]
    public async Task FS19_TwoMultiLineReservesNamingTheSameProductsInOppositeOrder_BothSucceedWithNoDeadlock()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "race-fs19");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var productX = $"OPPX-{i}";
            var productY = $"OPPY-{i}";
            await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", productX, units: 10);
            await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", productY, units: 10);

            var orderA = $"ORD-{700000 + (i * 2)}";
            var orderB = $"ORD-{700000 + (i * 2) + 1}";

            await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });

            // Order A names [X, Y]; order B names [Y, X] — opposite request order.
            var requestA = RpcJson.Serialize(new StockReserveRequestPayload(orderA, "RETAILER1", "ACME", [new StockReserveRequestLine(productX, 2), new StockReserveRequestLine(productY, 2)]));
            var requestB = RpcJson.Serialize(new StockReserveRequestPayload(orderB, "RETAILER1", "ACME", [new StockReserveRequestLine(productY, 2), new StockReserveRequestLine(productX, 2)]));

            var taskA = FulfillmentHostFixture.RequestBareAsync(connectionA, StockSubjects.StockReserve, requestA, BuildHeaders(), TimeSpan.FromSeconds(15));
            var taskB = FulfillmentHostFixture.RequestBareAsync(connectionB, StockSubjects.StockReserve, requestB, BuildHeaders(), TimeSpan.FromSeconds(15));

            var results = await Task.WhenAll(taskA, taskB);

            var payloadA = RpcJson.Deserialize<StockReserveReplyPayload>(results[0].Data!);
            var payloadB = RpcJson.Deserialize<StockReserveReplyPayload>(results[1].Data!);

            Assert.Equal("accepted", payloadA.Outcome);
            Assert.Equal("accepted", payloadB.Outcome);
        }

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders() => BuildHeaders(UniqueId.New());

    private static NatsHeaders BuildHeaders(UniqueId correlationId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", UniqueId.New().Value.ToString() },
    };
}
