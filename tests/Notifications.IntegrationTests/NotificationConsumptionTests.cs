using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using OrderToCash.Notifications.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Real Kafka + real MS-SQL, end to end — feature 23's own acceptance
/// bullet 3 ("idempotent by eventId") and R17/R18, proven against a real
/// broker and a real database rather than fakes, mirroring the shape of
/// Orders' own <c>SagaConsumptionTests</c>.
/// </summary>
[Collection(NotificationsCollection.Name)]
public sealed class NotificationConsumptionTests(MsSqlContainerFixture mssql, KafkaContainerFixture kafka)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ConsumesARealOrderPlacedFact_SendsExactlyOnceAndRecordsTheLedgerRow()
    {
        var (host, connectionString, sender) = await NotificationConsumptionTestSupport.StartHostAsync(mssql, kafka, "consume");
        try
        {
            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var envelope = BuildOrderPlacedEnvelope(eventId, correlationId);

            await NotificationConsumptionTestSupport.PublishAsync(
                kafka.BootstrapServers,
                NotificationFactTopics.OrdersFacts,
                NotificationConsumptionTestSupport.SerializeEnvelope(envelope));

            var sendCount = await NotificationConsumptionTestSupport.WaitForSenderCallCountAsync(sender, atLeast: 1, _wait);
            Assert.Equal(1, sendCount);

            var sent = Assert.Single(sender.SentMessages);
            Assert.Equal($"{eventId}@order-to-cash", sent.MessageId);
            Assert.Contains("Order ORD-CONSUME-1 placed", sent.Subject, StringComparison.Ordinal);

            var ledgerRows = await NotificationConsumptionTestSupport.WaitForLedgerRowCountAsync(mssql, connectionString, eventId, atLeast: 1, _wait);
            Assert.Equal(1, ledgerRows);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>NS8's absence half, in-process redelivery — the rebalance case #7's own review found the ONLY scenario an in-memory ledger survives.</summary>
    [Fact]
    public async Task ARedeliveredEventId_InTheSameRunningProcess_SendsNoSecondEmail()
    {
        var (host, connectionString, sender) = await NotificationConsumptionTestSupport.StartHostAsync(mssql, kafka, "redelivery");
        try
        {
            var eventId = Guid.NewGuid();
            var envelope = BuildOrderPlacedEnvelope(eventId, Guid.NewGuid());
            var bytes = NotificationConsumptionTestSupport.SerializeEnvelope(envelope);

            await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, NotificationFactTopics.OrdersFacts, bytes);
            await NotificationConsumptionTestSupport.WaitForSenderCallCountAsync(sender, atLeast: 1, _wait);

            // A REAL republish of the identical eventId — a genuine Kafka
            // redelivery of the same fact, not a second in-process call.
            await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, NotificationFactTopics.OrdersFacts, bytes);

            // Give the redelivery every opportunity to (wrongly) cause a
            // second send before asserting it did not.
            await Task.Delay(5_000);

            Assert.Equal(1, sender.CallCount);
            Assert.Equal(1, await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssql, connectionString, eventId));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// The durable-ledger inheritance's own proof: a fact processed by app1,
    /// then redelivered AFTER app1 is closed, is NOT re-sent by a
    /// freshly-compiled app2 sharing the same durable MS-SQL ledger — the
    /// exact scenario #7's round-1 in-memory store failed (three emails for
    /// one eventId). This service inherits the durable table from phase 6,
    /// so there is no equivalent defect to reproduce here — this test proves
    /// the inheritance rather than a regression fix.
    /// </summary>
    [Fact]
    public async Task ARedeliveredEventId_AfterARestart_IsNotResentByAFreshlyCompiledHostSharingTheSameDurableLedger()
    {
        var (host1, connectionString, sender1) = await NotificationConsumptionTestSupport.StartHostAsync(mssql, kafka, "restart");
        var eventId = Guid.NewGuid();
        var envelope = BuildOrderPlacedEnvelope(eventId, Guid.NewGuid());
        var bytes = NotificationConsumptionTestSupport.SerializeEnvelope(envelope);

        await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, NotificationFactTopics.OrdersFacts, bytes);
        await NotificationConsumptionTestSupport.WaitForSenderCallCountAsync(sender1, atLeast: 1, _wait);
        Assert.Equal(1, sender1.CallCount);

        await host1.StopAsync();
        host1.Dispose();

        // A fresh Notifications host, SAME database (the durable ledger),
        // SAME Kafka consumer group ("notifications") — a genuine process
        // restart, not a second call inside the same object graph.
        var builder2 = OrderToCash.Notifications.NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
            });

        var sender2 = new NotificationConsumptionTestSupport.FakeNotificationSender();
        builder2.Services.Replace(ServiceDescriptor.Singleton<INotificationSender>(sender2));

        var host2 = builder2.Build();
        await host2.StartAsync();
        try
        {
            // Unlike a brand-new group (WarmUpAsync's own concern), this
            // group ("notifications") already holds a COMMITTED offset from
            // host1's successful processing above — AutoOffsetReset.Latest
            // never applies here, host2 simply resumes from that committed
            // position once its own partition assignment completes, however
            // long that takes. A flat wait after publishing is therefore
            // sufficient (no "publish before assignment" race is possible
            // for an EXISTING group), but generous, given assignment for a
            // rejoining group was observed to take several seconds against a
            // cold broker (WarmUpAsync's own remarks).
            await Task.Delay(3_000);

            // The SAME eventId, republished — a real redelivery.
            await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, NotificationFactTopics.OrdersFacts, bytes);

            // Every opportunity to (wrongly) send a second time.
            await Task.Delay(15_000);

            Assert.Equal(0, sender2.CallCount);
            Assert.Equal(1, sender1.CallCount);
            Assert.Equal(1, await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssql, connectionString, eventId));
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
        }
    }

    /// <summary>domain-model.md §7.3 — stock.* is deliberately NOT notified on. Guards the exclusion filter itself, not merely "nothing observed by accident".</summary>
    [Fact]
    public async Task AStockReservedFact_IsAcknowledgedButNeverDispatched()
    {
        var (host, connectionString, sender) = await NotificationConsumptionTestSupport.StartHostAsync(mssql, kafka, "suppression");
        try
        {
            var eventId = Guid.NewGuid();
            var envelope = new Envelope<StockReservedPayload>(
                eventId,
                "stock.reserved.v1",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new StockReservedPayload("ORD-SUPPRESS-1", "COMP01", [new ReservationRef(Guid.NewGuid(), "SKU-1", 1)]));

            await NotificationConsumptionTestSupport.PublishAsync(
                kafka.BootstrapServers,
                NotificationFactTopics.FulfillmentFacts,
                NotificationConsumptionTestSupport.SerializeEnvelope(envelope));

            // Prove the consumer is genuinely alive and reading THIS topic
            // by ALSO publishing a real notified fact afterwards, and
            // waiting for THAT to arrive — a zero count from a consumer that
            // never subscribed would otherwise be indistinguishable from a
            // correctly-filtered zero count. StartHostAsync's own warm-up
            // already proves the group is assigned (on the orders topic),
            // but this control fact proves it specifically for the
            // FULFILLMENT topic this test actually publishes to.
            var controlEventId = Guid.NewGuid();
            var controlEnvelope = new Envelope<OrderDespatchedPayload>(
                controlEventId,
                "order.despatched.v1",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new OrderDespatchedPayload("ORD-SUPPRESS-1", "DES-SUPPRESS-1", DateTimeOffset.UtcNow, "COMP01", "CarrefourEs", [new DespatchLine("SKU-1", 1)]));

            await NotificationConsumptionTestSupport.PublishAsync(
                kafka.BootstrapServers,
                NotificationFactTopics.FulfillmentFacts,
                NotificationConsumptionTestSupport.SerializeEnvelope(controlEnvelope));

            await NotificationConsumptionTestSupport.WaitForSenderCallCountAsync(sender, atLeast: 1, _wait);

            Assert.Equal(1, sender.CallCount);
            Assert.Contains("despatched", sender.SentMessages.Single().Subject, StringComparison.Ordinal);
            Assert.Equal(0, await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssql, connectionString, eventId));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>N6 — a failed send deletes the just-recorded ledger row against a REAL database, so a real redelivery gets a genuinely fresh attempt and succeeds exactly once.</summary>
    [Fact]
    public async Task AFailedSend_DeletesTheLedgerRowSoARealRedeliveryGetsAFreshAttemptAndSucceedsExactlyOnce()
    {
        var (host, connectionString, sender) = await NotificationConsumptionTestSupport.StartHostAsync(mssql, kafka, "n6");
        try
        {
            var eventId = Guid.NewGuid();
            var envelope = BuildOrderPlacedEnvelope(eventId, Guid.NewGuid());
            var bytes = NotificationConsumptionTestSupport.SerializeEnvelope(envelope);

            sender.ThrowOnNextSend = new InvalidOperationException("simulated SMTP failure");

            await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, NotificationFactTopics.OrdersFacts, bytes);

            // The failed attempt commits the offset (NotificationFactsConsumer
            // never sees the throw as a Kafka-level handler failure — the
            // command handler swallows nothing but NotificationDispatchService
            // DOES rethrow, and the consumer's own catch-and-re-enter loop
            // means the offset is never stored for a thrown handler). Poll
            // for the ledger row to disappear again (inserted, then deleted).
            var deadline = DateTime.UtcNow + _wait;
            var rowsAfterFailure = -1;
            while (DateTime.UtcNow < deadline)
            {
                rowsAfterFailure = await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssql, connectionString, eventId);
                if (rowsAfterFailure == 0 && sender.CallCount == 0)
                {
                    break;
                }

                await Task.Delay(200);
            }

            Assert.Equal(0, rowsAfterFailure);
            Assert.Equal(0, sender.CallCount);

            // A REAL redelivery — the failed handler never stored its
            // offset, so the consumer's own retry loop re-delivers the SAME
            // message without any help from this test.
            var sendCount = await NotificationConsumptionTestSupport.WaitForSenderCallCountAsync(sender, atLeast: 1, TimeSpan.FromSeconds(30));

            Assert.Equal(1, sendCount);
            Assert.Equal(1, await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssql, connectionString, eventId));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private static Envelope<OrderPlacedPayload> BuildOrderPlacedEnvelope(Guid eventId, Guid correlationId) => new(
        eventId,
        "order.placed.v1",
        Guid.NewGuid(),
        correlationId,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        new OrderPlacedPayload(
            "ORD-CONSUME-1",
            "CarrefourEs",
            "COMP01",
            "1234567890123",
            "9876543210987",
            "USD",
            DateTimeOffset.UtcNow,
            [],
            100,
            0,
            100));
}
