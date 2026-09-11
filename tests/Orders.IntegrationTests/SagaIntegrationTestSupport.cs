using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Shared harness for the saga integration suites (design.md §8.1) — builds
/// the REAL host (<c>AddOrdersOutbox</c> + <c>AddOrdersAcceptance</c> +
/// <c>AddOrdersSaga</c> + <c>AddDispatcher</c>, <c>OrdersHost.CreateBuilder</c>
/// itself), against real Kafka, real NATS and a fresh real MS-SQL database,
/// with the saga's own timings shortened so the suite stays fast.
/// </summary>
internal static class SagaIntegrationTestSupport
{
    public static async Task<(IHost Host, string ConnectionString)> StartHostAsync(
        MsSqlContainerFixture mssql,
        KafkaContainerFixture kafka,
        NatsContainerFixture nats,
        string databaseNameSuffix,
        Action<OrdersSagaOptions>? configureSaga = null)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_saga_{databaseNameSuffix}_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var builder = OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 200;
            },
            configureAcceptance: options => options.Nats.Url = nats.Url,
            configureSaga: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Command.TimeoutMs = 1_000;
                options.Command.BackoffMs = 100;
                options.Command.MaxAttempts = 3;
                options.Command.LeaseMs = 5_000;
                options.Sweeper.IntervalMs = 500;
                options.Sweeper.PendingGraceMs = 300;
                options.Sweeper.ParkRetryCapMs = 5_000;
                options.Sweeper.BatchSize = 20;
                // OR1's DLQ producer — DEFAULTS to localhost:9092, which on
                // a developer machine running docker-compose.infra.yml's own
                // persistent Kafka is a REAL, DIFFERENT broker than this
                // ephemeral Testcontainers one. Left unset, a dead-letter
                // publish silently succeeds against the wrong broker and
                // every SagaDeadLetterTests assertion against a real DLQ
                // message times out despite the committed-offset assertion
                // passing (PublishAsync never throws).
                options.DeadLetter.BootstrapServers = kafka.BootstrapServers;
                configureSaga?.Invoke(options);
            });

        var host = builder.Build();
        await host.StartAsync();

        // BackgroundService.StartAsync returns as soon as ExecuteAsync is
        // SCHEDULED, not once OrdersCreateResponder's three real NATS
        // subscriptions (orders.create, catalog.reference.list,
        // orders.cancel — all three started together from the same
        // Task.WhenAll in ExecuteAsync) have actually landed server-side —
        // the identical subscribe-side race BillingHostFixture and
        // FulfillmentHostFixture close for their own responders, and that
        // OrdersCreateAcceptanceTests.WaitUntilOrdersCreateReachableAsync
        // closes locally for its own directly-built host. This shared saga
        // harness never closed it. Every caller of StartHostAsync that
        // warms up via PlaceOrderAsync (an in-process dispatcher call) first
        // happens to absorb enough real wall-clock time for the race to
        // have already resolved by the time it sends a NATS RPC — the one
        // caller that does not, OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound,
        // sends its orders.cancel RPC immediately after this method
        // returns and could lose the race under load, observed live as
        // NATS.Client.Core.NatsNoRespondersException. Deterministic proof
        // of the mechanism, and of this fix's own correctness against an
        // artificially delayed subscriber, is in
        // OrdersCancelResponderReadinessRaceTests.
        await using (var probeConnection = new NatsConnection(new NatsOpts { Url = nats.Url }))
        {
            await WaitUntilOrdersResponderReachableAsync(probeConnection, CancellationToken.None);
        }

        return (host, connectionString);
    }

    /// <summary>
    /// Blocks until a real request/reply round trip to <c>orders.cancel</c>
    /// succeeds — proof, not inference, that <see cref="OrdersCreateResponder"/>'s
    /// subscription (and, since all three of its subjects are started
    /// together from the same <c>ExecuteAsync</c>, its siblings' too) is
    /// genuinely live server-side. The probe order id is a fresh
    /// <see cref="Guid"/> no order can ever hold, so the only possible
    /// success reply is a harmless <c>NOT_FOUND</c> error body — never a
    /// real cancellation. Retried exactly like
    /// <c>BillingHostFixture.WaitUntilReachableAsync</c>/
    /// <c>FulfillmentHostFixture.WaitUntilReachableAsync</c>/
    /// <c>OrdersCreateAcceptanceTests.WaitUntilOrdersCreateReachableAsync</c>:
    /// <see cref="NatsNoReplyException"/> and <see cref="NatsNoRespondersException"/>
    /// both mean "not subscribed yet," not "give up."
    /// </summary>
    public static Task WaitUntilOrdersResponderReachableAsync(INatsConnection connection, CancellationToken cancellationToken)
    {
        var probe = RpcJson.Serialize(new OrdersCancelRequestPayload(Guid.NewGuid(), OrderReference: null, "operator_cancelled", Note: null));
        return WaitUntilReachableAsync(connection, RpcSubjects.OrdersCancel, probe, cancellationToken);
    }

    /// <summary>
    /// The generic retry/catch loop <see cref="WaitUntilOrdersResponderReachableAsync"/>
    /// specialises to <c>orders.cancel</c> — extracted so
    /// <c>OrdersCancelResponderReadinessRaceTests</c> can arm this loop's own
    /// correctness against a deliberately delayed, synthetic subscriber,
    /// deterministically, without needing the real production responder.
    /// Same shape as <c>BillingHostFixture.WaitUntilReachableAsync</c>/
    /// <c>FulfillmentHostFixture.WaitUntilReachableAsync</c>/
    /// <c>OrdersCreateAcceptanceTests.WaitUntilOrdersCreateReachableAsync</c>,
    /// with ONE deliberate strengthening those three precedents do not have:
    /// a fixed pacing delay after EVERY failed attempt, not only a per-attempt
    /// request timeout. <see cref="NatsNoRespondersException"/> is the
    /// server's IMMEDIATE "definitely nobody subscribed" sentinel — it does
    /// not wait out the request's own timeout — so a loop that paces only via
    /// that timeout can burn through all its attempts in a few milliseconds
    /// and give up long before a genuinely slow subscription lands. Found
    /// arming <c>OrdersCancelResponderReadinessRaceTests</c> against a
    /// synthetic 300ms-delayed subscriber: the unpaced first draft of this
    /// loop exhausted 100 attempts and threw in ~1ms. <see cref="NatsNoReplyException"/>
    /// and <see cref="NatsNoRespondersException"/> both mean "not subscribed
    /// yet," not "give up."
    /// </summary>
    public static async Task WaitUntilReachableAsync(INatsConnection connection, string subject, byte[] probe, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    subject,
                    probe,
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
            }
            catch (NatsNoRespondersException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"'{subject}' never became reachable.");
    }

    /// <summary>
    /// Stops <paramref name="host"/> AND CONFIRMS its own consumer has
    /// actually LEFT <paramref name="groupId"/> before returning — never
    /// merely that <c>StopAsync</c>/<c>Dispose</c> were called and trusted.
    /// The Notifications-copy of this class (feature <c>observability_reliability</c>,
    /// review round 4) proved directly, against the real
    /// <c>KafkaFactStreamSubscriber</c>, that <c>host.StopAsync()</c> can
    /// return successfully — matching .NET's own default
    /// <c>HostOptions.ShutdownTimeout</c> (30s) almost to the millisecond —
    /// WHILE the broker is still unreachable and the subscriber's own
    /// <c>finally { consumer.Close(); }</c> has not completed, leaving a
    /// stale member in the group. Every host built by
    /// <see cref="StartHostAsync"/> joins the SAME literal production group
    /// (<c>"orders.saga"</c>, <c>KafkaFactStreamSubscriber.cs:130</c>),
    /// shared sequentially across every <see cref="SagaCollection"/> test —
    /// so a stale member left by one test's teardown can block the NEXT
    /// test's own host from ever being assigned a partition, exactly the
    /// mechanism the Notifications copy's own `zombieprobe` reproduced
    /// directly (a silent member held a fresh topic's partitions for the
    /// full 90s a DLQ test budgets). This ported copy closes the SAME gap
    /// here, at its class, rather than leaving it live in this project only
    /// because THIS project's suite happened to stay green.
    /// </summary>
    public static async Task StopHostAndWaitForGroupToClearAsync(IHost host, KafkaContainerFixture kafka, string groupId = "orders.saga", TimeSpan? timeout = null)
    {
        await host.StopAsync();
        host.Dispose();

        var budget = timeout ?? TimeSpan.FromSeconds(150);
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + budget;
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await admin.DescribeConsumerGroupsAsync([groupId], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                var description = result.ConsumerGroupDescriptions.SingleOrDefault(g => g.GroupId == groupId);
                if (description is null || description.Members.Count == 0)
                {
                    return;
                }
            }
            catch (KafkaException)
            {
                // DescribeConsumerGroupsAsync itself can transiently fail
                // under the SAME contention that motivates this wait —
                // retry within the budget rather than surface a spurious
                // failure from the PROBE itself.
            }

            await Task.Delay(300);
        }

        throw new TimeoutException(
            $"Consumer group '{groupId}' still reported members {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s after this test's own host was stopped — its teardown left a stale member that would otherwise block the NEXT test's rebalance (observed directly in the Notifications copy's own `zombieprobe` reproduction: a silent member can hold every partition of a topic for well over 90s).");
    }

    /// <summary>Places an order through the REAL <see cref="PlaceOrderCommandHandler"/>, in-process — the caller must already have a stand-in <c>fulfillment.stock.check</c> responder running.</summary>
    public static async Task<PlaceOrderResult> PlaceOrderAsync(IHost host, IReadOnlyList<PlaceOrderRequestLine>? lines = null, CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var command = new PlaceOrderCommand(
            RequestId: null,
            OrderPersistenceTestSupport.RetailerCode,
            OrderPersistenceTestSupport.CompanyCode,
            OrderPersistenceTestSupport.Currency,
            lines ??
            [
                new PlaceOrderRequestLine(OrderPersistenceTestSupport.ProductCode1, new Quantity(2), UnitPriceMinorUnits: 1_000, LineDiscountMinorUnits: 50),
                new PlaceOrderRequestLine(OrderPersistenceTestSupport.ProductCode2, new Quantity(1), UnitPriceMinorUnits: 500, LineDiscountMinorUnits: 0),
            ],
            OrderDiscountMinorUnits: null,
            Notes: null);

        return await dispatcher.SendAsync<PlaceOrderCommand, PlaceOrderResult>(command, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> WaitForOrderStatusAsync(string connectionString, MsSqlContainerFixture mssql, Guid orderId, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string last = "<never observed>";

        while (DateTime.UtcNow < deadline)
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var status = await db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleOrDefaultAsync();
            if (status is not null)
            {
                last = status;
                if (string.Equals(status, expectedStatus, StringComparison.Ordinal))
                {
                    return status;
                }
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Order {orderId} never reached status '{expectedStatus}' within {timeout}. Last observed: '{last}'.");
    }

    public static async Task<int> WaitForSagaCommandCountAsync(string connectionString, MsSqlContainerFixture mssql, Guid orderId, string command, string status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var count = await db.SagaCommands.CountAsync(c => c.OrderId == orderId && c.Command == command && c.Status == status);
            if (count > 0)
            {
                return count;
            }

            await Task.Delay(150);
        }

        return 0;
    }

    public static async Task<int> CountOutboxEventsAsync(string connectionString, MsSqlContainerFixture mssql, Guid aggregateId, string eventType)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.OutboxMessages.CountAsync(m => m.AggregateId == aggregateId && m.EventType == eventType);
    }

    public static async Task<int> WaitForOutboxEventCountAsync(string connectionString, MsSqlContainerFixture mssql, Guid aggregateId, string eventType, int atLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = 0;

        while (DateTime.UtcNow < deadline)
        {
            last = await CountOutboxEventsAsync(connectionString, mssql, aggregateId, eventType);
            if (last >= atLeast)
            {
                return last;
            }

            await Task.Delay(150);
        }

        return last;
    }

    /// <summary>
    /// Waits for ANY <c>saga_ignored_facts</c> row for <paramref name="correlationId"/>
    /// with the given <paramref name="marker"/> — adequate wherever the caller
    /// publishes exactly one candidate fact per correlation (SO8's unknown-order
    /// probe, and the single stray redelivery in
    /// <c>SagaCompensationStockRejectedTests</c> — checked, review round 3 D4).
    /// Do NOT use this inside a loop that publishes several facts for the SAME
    /// correlation and then asserts on the row for ONE of them: <c>count &gt; 0</c>
    /// is satisfied by an EARLIER iteration's own row and the wait stops gating
    /// anything from the second iteration onward. Use the event-type-filtered
    /// overload below there instead (review round 3 D4).
    /// </summary>
    public static Task<int> WaitForSagaIgnoredFactCountAsync(string connectionString, MsSqlContainerFixture mssql, Guid correlationId, string marker, TimeSpan timeout) =>
        WaitForSagaIgnoredFactCountAsync(connectionString, mssql, correlationId, eventType: null, marker, timeout);

    /// <summary>
    /// As the marker-only overload, but when <paramref name="eventType"/> is
    /// non-null the wait is gated on a row matching
    /// <c>(correlationId, eventType, marker)</c> exactly — the fix for review
    /// round 3 D4, where a marker-only wait inside a multi-fact loop returned
    /// on a PRIOR iteration's row and left the current iteration's assertion
    /// unguarded.
    /// </summary>
    public static async Task<int> WaitForSagaIgnoredFactCountAsync(string connectionString, MsSqlContainerFixture mssql, Guid correlationId, string? eventType, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var count = await db.SagaIgnoredFacts.CountAsync(f =>
                f.CorrelationId == correlationId &&
                f.Marker == marker &&
                (eventType == null || f.EventType == eventType));
            if (count > 0)
            {
                return count;
            }

            await Task.Delay(150);
        }

        return 0;
    }

    /// <summary>
    /// SO9's broker-side proof (design.md §3.3, review D1): sums the given
    /// consumer group's COMMITTED offset over every partition of a topic,
    /// read from the broker itself, never inferred from application-level
    /// behaviour such as a redelivery. Uses a THROWAWAY consumer configured
    /// with the SAME <paramref name="groupId"/> and calls
    /// <see cref="IConsumer{TKey,TValue}.Committed"/> — an OffsetFetch
    /// request only, which does not join the group, does not affect its
    /// partition assignment, and does not disturb the real subscriber
    /// (<c>KafkaFactStreamSubscriber</c>) in any way.
    /// <c>AdminClient.ListConsumerGroupOffsetsAsync</c> was tried first (the
    /// review's other suggested option) and crashed the test host natively
    /// with no managed stack trace when queried against a group with no
    /// prior member — reproduced twice, both times on the very first call,
    /// before this order's own fact even existed. <c>IConsumer.Committed</c>
    /// is the long-established, narrower API for exactly this read and does
    /// not exhibit it.
    /// A partition with no committed offset yet reports
    /// <see cref="Offset.IsSpecial"/> and contributes 0 — that is the
    /// correct "never committed" reading, not an error.
    /// </summary>
    public static async Task<long> ReadCommittedOffsetTotalAsync(string bootstrapServers, string topic, string groupId, int partitionCount, TimeSpan requestTimeout)
    {
        var (total, _) = await ReadCommittedOffsetsAsync(bootstrapServers, topic, groupId, partitionCount, requestTimeout);
        return total;
    }

    /// <summary>As <see cref="ReadCommittedOffsetTotalAsync"/>, also returning a human-readable per-partition breakdown for a failing assertion's message.</summary>
    public static Task<(long Total, string Description)> ReadCommittedOffsetsAsync(string bootstrapServers, string topic, string groupId, int partitionCount, TimeSpan requestTimeout) =>
        Task.Run(() =>
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = groupId,
                EnableAutoCommit = false, // a read-only probe: never stores, never commits, never joins the group.
            };

            using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();

            var partitions = Enumerable.Range(0, partitionCount)
                .Select(p => new TopicPartition(topic, new Partition(p)))
                .ToList();

            // Committed() issues a FindCoordinator lookup under the hood on
            // a freshly-built handle; the very first call after Build() can
            // race that lookup and come back "Broker: Not coordinator" —
            // transient, and gone on retry once the handle has cached the
            // group's coordinator. Not a property of the offset itself, so
            // retried here rather than surfaced as a flaky assertion.
            List<TopicPartitionOffset> committed = [];
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    committed = consumer.Committed(partitions, requestTimeout);
                    break;
                }
                catch (KafkaException) when (attempt < 5)
                {
                    Thread.Sleep(300);
                }
            }

            var total = committed.Sum(tpo => tpo.Offset.IsSpecial ? 0L : tpo.Offset.Value);
            var description = string.Join(", ", committed.Select(tpo => $"p{tpo.Partition.Value}={(tpo.Offset.IsSpecial ? "unset" : tpo.Offset.Value.ToString())}"));
            return (total, description);
        });

    /// <summary>Polls <see cref="ReadCommittedOffsetTotalAsync"/> until the group's committed offset total exceeds <paramref name="baseline"/>, or the timeout elapses (in which case the last observed total, possibly still equal to the baseline, is returned — the caller asserts).</summary>
    public static async Task<long> WaitForCommittedOffsetToExceedAsync(string bootstrapServers, string topic, string groupId, int partitionCount, long baseline, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = baseline;

        while (DateTime.UtcNow < deadline)
        {
            last = await ReadCommittedOffsetTotalAsync(bootstrapServers, topic, groupId, partitionCount, TimeSpan.FromSeconds(5));
            if (last > baseline)
            {
                return last;
            }

            await Task.Delay(300);
        }

        return last;
    }
}
