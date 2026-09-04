using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// design.md §8.1 — R25, SO8, and <c>saga.md</c> §6's redelivery table swept
/// literally: an order is driven to <c>completed</c> (past every one of the
/// ten consumed facts' own precondition), then EACH of the ten is
/// redelivered with a fresh <c>eventId</c> — R25's "an unmet precondition
/// changes nothing, issues nothing" holds uniformly, recorded with both
/// observed and expected status. A fact whose <c>correlationId</c> matches
/// no order is recorded <c>unknown_order</c> and acknowledged without
/// throwing (SO8).
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaPreconditionTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "precondition");
        try
        {
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(nats.Url, r => new StockReserveReplyPayload("accepted", r.OrderReference, Reservations: []), CancellationToken.None);
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(nats.Url, r => new CreditHoldReplyPayload("approved", r.OrderReference, r.Amount.Currency, 5_000_00, HeldAmount: r.Amount.Amount), CancellationToken.None);
            await using var despatchCreate = await StandInSagaResponders.StartDespatchCreateAsync(nats.Url, r => new DespatchCreateReplyPayload(r.OrderReference, "DES-000001", DateTimeOffset.UtcNow, Created: true, Lines: []), CancellationToken.None);
            await using var invoiceIssue = await StandInSagaResponders.StartInvoiceIssueAsync(nats.Url, r => new InvoiceIssueReplyPayload(r.OrderReference, "INV-000001", DateTimeOffset.UtcNow, r.Currency, 2_450, "issued", Created: true), CancellationToken.None);
            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(nats.Url, r => new StockReleaseReplyPayload("released", r.OrderReference, Released: []), CancellationToken.None);
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;
            var reference = placed.OrderReference.Value;

            await DriveToCompletedAsync(orderId, reference);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(0, await db.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId));
            }

            var facts = new (string EventType, string Topic, object Payload)[]
            {
                ("order.placed.v1", SagaFactTopics.OrdersFacts, new OrderPlacedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.BuyerGlnValue, OrderPersistenceTestSupport.SupplierGlnValue, "EUR", DateTimeOffset.UtcNow, [], 2_500, 50, 2_450)),
                ("stock.reserved.v1", SagaFactTopics.FulfillmentFacts, new StockReservedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [])),
                ("stock.rejected.v1", SagaFactTopics.FulfillmentFacts, new StockRejectedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "insufficient_stock")),
                ("credit.approved.v1", SagaFactTopics.BillingFacts, new CreditApprovedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 2_550_00)),
                ("credit.rejected.v1", SagaFactTopics.BillingFacts, new CreditRejectedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 100, "over_limit")),
                ("stock.released.v1", SagaFactTopics.FulfillmentFacts, new StockReleasedPayload(reference, OrderPersistenceTestSupport.CompanyCode, [], "credit_rejected")),
                ("order.despatched.v1", SagaFactTopics.FulfillmentFacts, new OrderDespatchedPayload(reference, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, [])),
                ("invoice.issued.v1", SagaFactTopics.BillingFacts, new InvoiceIssuedPayload(reference, "INV-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", [], 2_450, 0, 2_450)),
                ("payment.received.v1", SagaFactTopics.BillingFacts, new PaymentReceivedPayload(reference, "INV-000001", "PAY-000002", "EUR", 2_450, DateTimeOffset.UtcNow, "gateway")),
                ("credit.released.v1", SagaFactTopics.BillingFacts, new CreditReleasedPayload(reference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 5_000_00, "order_cancelled")),
            };

            var expectedIgnoredCount = 0;
            foreach (var (eventType, topic, payload) in facts)
            {
                expectedIgnoredCount++;
                await PublishAsync(eventType, topic, payload);

                // review round 3 D4: waiting on the marker alone is satisfied
                // by an EARLIER iteration's own row from iteration 2 onward,
                // so it gates nothing for this iteration. Wait for THIS
                // iteration's own (correlationId, eventType, marker) row —
                // the exact predicate the assertion below reads.
                await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, eventType, "precondition_unmet", _wait);

                await using var db = mssql.CreateDbContext(connectionString);
                var order = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal("completed", order.Status); // unchanged

                var ignoredRow = await db.SagaIgnoredFacts
                    .Where(f => f.CorrelationId == orderId && f.EventType == eventType && f.Marker == "precondition_unmet")
                    .OrderByDescending(f => f.RecordedAt)
                    .FirstAsync();
                Assert.Equal("completed", ignoredRow.ObservedStatus);
                Assert.NotNull(ignoredRow.ExpectedStatus);
                Assert.NotEqual("completed", ignoredRow.ExpectedStatus);

                Assert.True(await db.SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId && f.Marker == "precondition_unmet") >= expectedIgnoredCount);
            }

            return;

            async Task PublishAsync(string eventType, string topic, object payload) =>
                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, topic, eventType, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, payload, CancellationToken.None);

            async Task DriveToCompletedAsync(Guid id, string orderReference)
            {
                // review D3: wait for the relay-published order.placed.v1 to
                // have been CONSUMED (observed via the stock.reserve command
                // it issues) before publishing stock.reserved.v1 directly.
                // Both facts share the Placed precondition, so publishing
                // the second before the first has been consumed races the
                // outbox relay — whichever wins leaves the other recorded
                // precondition_unmet, which is what R25 counts as zero.
                // Mirrors SagaHappyPathTests.cs's synchronisation.
                await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, id, "stock.reserve", "sent", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new StockReservedPayload(orderReference, OrderPersistenceTestSupport.CompanyCode, []), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "stock_reserved", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.approved.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new CreditApprovedPayload(orderReference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 2_550_00), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "confirmed", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "order.despatched.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new OrderDespatchedPayload(orderReference, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, []), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "despatched", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.BillingFacts, "invoice.issued.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new InvoiceIssuedPayload(orderReference, "INV-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", [], 2_450, 0, 2_450), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "invoiced", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.BillingFacts, "payment.received.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new PaymentReceivedPayload(orderReference, "INV-000001", "PAY-000001", "EUR", 2_450, DateTimeOffset.UtcNow, "gateway"), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "paid", _wait);

                await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.BillingFacts, "credit.released.v1", id, Guid.NewGuid(), DateTimeOffset.UtcNow, new CreditReleasedPayload(orderReference, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 5_000_00, "order_cancelled"), CancellationToken.None);
                await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, id, "completed", _wait);
            }
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task SO8_AFactWhoseCorrelationIdMatchesNoOrder_IsRecordedUnknownOrderAndAcknowledgedWithoutThrowing()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "unknownorder");
        try
        {
            var unknownOrderId = Guid.NewGuid();

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.reserved.v1",
                unknownOrderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new StockReservedPayload("ORD-999999", OrderPersistenceTestSupport.CompanyCode, []),
                CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(connectionString, mssql, unknownOrderId, "unknown_order", _wait);

            await using var db = mssql.CreateDbContext(connectionString);
            var row = await db.SagaIgnoredFacts.SingleAsync(f => f.CorrelationId == unknownOrderId);
            Assert.Equal("unknown_order", row.Marker);
            Assert.Null(row.OrderId);
            Assert.Null(row.ObservedStatus);
            Assert.Null(row.ExpectedStatus);

            // Placing a DIFFERENT, real order afterwards still works — the
            // unknown-order fact never crashed the consume loop.
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            Assert.Equal("placed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, placed.OrderId.Value, "placed", TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
