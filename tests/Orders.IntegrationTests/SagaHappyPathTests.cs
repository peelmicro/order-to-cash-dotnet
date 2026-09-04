using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// design.md §8.1 — R19-R24, over real Kafka, real NATS and real MS-SQL.
/// Places an order through the real handler, lets the real outbox relay
/// publish <c>order.placed.v1</c>, and drives the whole happy path to
/// <c>completed</c> — the shared matrix's case names cited verbatim per
/// step, since the scenario is inherently one continuous sequence
/// (tasks.md L2).
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaHappyPathTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task R19_R24_HappyPath_AdvancesTheOrderThroughEveryStatusIssuingEachOwedCommandAndEmittingExactlyOneOrderConfirmedAndOneOrderCompleted()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "happy");
        try
        {
            StockReserveRequestPayload? observedReserve = null;
            await using var stockReserve = await StandInSagaResponders.StartStockReserveAsync(
                nats.Url,
                request =>
                {
                    observedReserve = request;
                    return new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []);
                },
                CancellationToken.None);

            CreditHoldRequestPayload? observedHold = null;
            await using var creditHold = await StandInSagaResponders.StartCreditHoldAsync(
                nats.Url,
                request =>
                {
                    observedHold = request;
                    return new CreditHoldReplyPayload("approved", request.OrderReference, request.Amount.Currency, 5_000_00, HeldAmount: request.Amount.Amount);
                },
                CancellationToken.None);

            DespatchCreateRequestPayload? observedDespatch = null;
            await using var despatchCreate = await StandInSagaResponders.StartDespatchCreateAsync(
                nats.Url,
                request =>
                {
                    observedDespatch = request;
                    return new DespatchCreateReplyPayload(request.OrderReference, "DES-000001", DateTimeOffset.UtcNow, Created: true, Lines: []);
                },
                CancellationToken.None);

            InvoiceIssueRequestPayload? observedInvoice = null;
            await using var invoiceIssue = await StandInSagaResponders.StartInvoiceIssueAsync(
                nats.Url,
                request =>
                {
                    observedInvoice = request;
                    return new InvoiceIssueReplyPayload(request.OrderReference, "INV-000001", DateTimeOffset.UtcNow, request.Currency, 2_450, "issued", Created: true);
                },
                CancellationToken.None);

            await using var stockRelease = await StandInSagaResponders.StartStockReleaseAsync(
                nats.Url,
                request => new StockReleaseReplyPayload("released", request.OrderReference, Released: []),
                CancellationToken.None);

            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // *issues stock.reserve for every line on order.placed.v1 and leaves the order in placed* (R19)
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);
            Assert.NotNull(observedReserve);
            Assert.Equal(2, observedReserve!.Lines.Count);
            Assert.Equal("placed", await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "placed", TimeSpan.FromSeconds(2)));

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.reserved.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new StockReservedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, []),
                CancellationToken.None);

            // *moves placed to stock_reserved and issues credit.hold for the order total* (R20)
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "stock_reserved", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "credit.hold", "sent", _wait);
            Assert.NotNull(observedHold);
            Assert.Equal(2_450, observedHold!.Amount.Amount);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.BillingFacts,
                "credit.approved.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new CreditApprovedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "CR-000001", "EUR", 2_450, 2_550_00),
                CancellationToken.None);

            // *moves stock_reserved through credit_approved to confirmed, emits exactly one order.confirmed.v1 and issues despatch.create* (R21)
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "confirmed", _wait);
            Assert.Equal(1, await SagaIntegrationTestSupport.WaitForOutboxEventCountAsync(connectionString, mssql, orderId, "order.confirmed.v1", atLeast: 1, _wait));
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "despatch.create", "sent", _wait);
            Assert.NotNull(observedDespatch);
            Assert.Equal(placed.OrderReference.Value, observedDespatch!.OrderReference);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "order.despatched.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new OrderDespatchedPayload(placed.OrderReference.Value, "DES-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.CompanyCode, OrderPersistenceTestSupport.RetailerCode, []),
                CancellationToken.None);

            // *moves confirmed to despatched and issues invoice.issue* (R22)
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "despatched", _wait);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "invoice.issue", "sent", _wait);
            Assert.NotNull(observedInvoice);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.BillingFacts,
                "invoice.issued.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new InvoiceIssuedPayload(placed.OrderReference.Value, "INV-000001", DateTimeOffset.UtcNow, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", [], 2_450, 0, 2_450),
                CancellationToken.None);

            // *moves despatched to invoiced and issues no further command while awaiting a remittance* (R23)
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "invoiced", _wait);
            await Task.Delay(1_000); // no command is EVER owed here — a short settle window, not a poll target.
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                Assert.Equal(4, await db.SagaCommands.CountAsync(c => c.OrderId == orderId)); // exactly the four owed so far: reserve, hold, despatch, invoice — none for invoice.issued.v1 itself (R23).
            }

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.BillingFacts,
                "payment.received.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new PaymentReceivedPayload(placed.OrderReference.Value, "INV-000001", "PAY-000001", "EUR", 2_450, DateTimeOffset.UtcNow, "gateway"),
                CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "paid", _wait);

            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.BillingFacts,
                "credit.released.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new CreditReleasedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.RetailerCode, OrderPersistenceTestSupport.CompanyCode, "EUR", 2_450, 5_000_00, "order_cancelled"),
                CancellationToken.None);

            // *moves invoiced to paid then paid to completed and emits exactly one order.completed.v1* (R24, integration half)
            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "completed", _wait);
            Assert.Equal(1, await SagaIntegrationTestSupport.WaitForOutboxEventCountAsync(connectionString, mssql, orderId, "order.completed.v1", atLeast: 1, _wait));
            Assert.Equal(1, await SagaIntegrationTestSupport.CountOutboxEventsAsync(connectionString, mssql, orderId, "order.confirmed.v1"));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
