using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Persistence;
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
                configureSaga?.Invoke(options);
            });

        var host = builder.Build();
        await host.StartAsync();

        return (host, connectionString);
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
