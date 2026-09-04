using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>design.md §8.1 — R26: cancelled with reason <c>stock_rejected</c>, EMPTY compensation steps, and the release-subject stand-in observes ZERO requests — including after a redelivery of <c>stock.rejected.v1</c> against <c>cancelled</c>.</summary>
[Collection(SagaCollection.Name)]
public sealed class SagaCompensationStockRejectedTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task R26_CancelsWithReasonStockRejectedAndIssuesNoStockReleaseCommand()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "stockrej");
        try
        {
            StockReserveRequestPayload? observedReserve = null;
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url,
                request =>
                {
                    observedReserve = request;
                    return new StockReserveReplyPayload("rejected", request.OrderReference, Shortages: []);
                },
                CancellationToken.None);

            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []),
                CancellationToken.None);

            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            Assert.NotNull(observedReserve);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.rejected.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new StockRejectedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "insufficient_stock"),
                CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var row = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("stock_rejected", row.CancellationReason);
            }

            var cancelledEnvelope = await SagaIntegrationTestSupport.WaitForOutboxEventCountAsync(connectionString, mssql, orderId, "order.cancelled.v1", atLeast: 1, _wait);
            Assert.Equal(1, cancelledEnvelope);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
                Assert.Contains("\"compensationSteps\":[]", cancelledRow.Payload, StringComparison.Ordinal);
            }

            // No stock.release command was ever enqueued (R26's "SHALL NOT").
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(0, await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "stock.release"));
            }

            // Redeliver stock.rejected.v1 against `cancelled` — R25's terminal-state ignore. Still zero release requests observed.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.rejected.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new StockRejectedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "insufficient_stock"),
                CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "precondition_unmet", _wait);

            await Task.Delay(1_000); // settle window — nothing should change.
            Assert.Empty(stockRelease.ObservedRequests);
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(0, await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == "stock.release"));
                Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1"));
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
