using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>design.md §8.1 — R27, R28, SO6, SO7: release-then-cancel in causal order, exactly one <c>stock_released</c> compensation step built from the observed fact, and the business-rejected <c>credit.hold</c> is not retried.</summary>
[Collection(SagaCollection.Name)]
public sealed class SagaCompensationCreditRejectedTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task R27_R28_SO6_SO7_ReleasesThenCancelsInCausalOrderWithOneCompensationStepAndNeverRetriesTheRejectedHold()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "creditrej");
        try
        {
            var holdCallCount = 0;
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                request =>
                {
                    Interlocked.Increment(ref holdCallCount);
                    return new CreditHoldReplyPayload("rejected", request.OrderReference, request.Amount.Currency, 100, Reason: "over_limit");
                },
                CancellationToken.None);

            StockReleaseRequestPayload? observedRelease = null;
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                request =>
                {
                    observedRelease = request;
                    return new StockReleaseReplyPayload("released", request.OrderReference, Released: []);
                },
                CancellationToken.None);

            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url,
                request => new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []),
                CancellationToken.None);

            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // review A6: wait for the relay-published order.placed.v1 to have
            // been consumed (observed via the stock.reserve command it
            // issues) before publishing stock.reserved.v1 directly — both
            // facts share the Placed precondition, so an unsynchronised
            // publish here races the outbox relay exactly as D3 did.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);

            // SO6 — a business rejection is marked sent, never retried: exactly one call, order unchanged from stock_reserved.
            await Task.Delay(1_500); // longer than the in-line retry budget would need if (incorrectly) retried.
            Assert.Equal(1, holdCallCount);
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.rejected.v1", orderId, Guid.NewGuid(), DateTimeOffset.UtcNow,
                new CreditRejectedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100, "over_limit"), CancellationToken.None);

            // R27 — issues stock.release, stays stock_reserved.
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait);
            Assert.NotNull(observedRelease);
            Assert.Equal("stock_reserved", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", TimeSpan.FromSeconds(1)));

            var releasedFactEventId = Guid.NewGuid();
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.released.v1", orderId, releasedFactEventId, DateTimeOffset.UtcNow,
                new StockReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "credit_rejected"), CancellationToken.None,
                eventId: releasedFactEventId);

            // R28/SO7 — cancels with reason credit_rejected only after stock.released.v1 arrives.
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal("credit_rejected", row.CancellationReason);

            var cancelledRow = await db.OutboxMessages.SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.cancelled.v1");
            using var payloadDoc = JsonDocument.Parse(cancelledRow.Payload);
            var steps = payloadDoc.RootElement.GetProperty("compensationSteps");
            Assert.Equal(1, steps.GetArrayLength());
            var step = steps[0];
            Assert.Equal("stock_released", step.GetProperty("step").GetString());
            Assert.Equal(releasedFactEventId, step.GetProperty("eventId").GetGuid());

            // Causal order: the emitted order.cancelled.v1 names stock.released.v1's own eventId as its causationId.
            Assert.Equal(releasedFactEventId, cancelledRow.CausationId);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
