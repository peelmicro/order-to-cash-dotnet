using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>Starts the REAL <see cref="ProjectorHost"/> against real containers, and publishes raw envelopes to the real fact topics.</summary>
public static class ProjectorTestHost
{
    public static async Task<IHost> StartAsync(
        KafkaContainerFixture kafka,
        NatsContainerFixture nats,
        string mongoConnectionUri,
        string mongoDatabase,
        Action<OrderToCash.Projector.Infrastructure.ProjectorOptions>? configure = null)
    {
        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Nats.Url = nats.Url;
                options.Mongo.ConnectionUri = mongoConnectionUri;
                options.Mongo.Database = mongoDatabase;
                configure?.Invoke(options);
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Stops <paramref name="host"/> AND CONFIRMS its own consumer has
    /// actually LEFT <paramref name="groupId"/> before returning — never
    /// merely that <c>StopAsync</c>/<c>Dispose</c> were called and trusted.
    /// Ported from the Notifications copy (feature <c>observability_reliability</c>,
    /// review round 4), which proved directly, against the real
    /// <c>KafkaFactStreamSubscriber</c>, that <c>host.StopAsync()</c> can
    /// return successfully — matching .NET's own default
    /// <c>HostOptions.ShutdownTimeout</c> (30s) — WHILE the broker is still
    /// unreachable and the subscriber's own <c>finally { consumer.Close(); }</c>
    /// has not completed, leaving a stale member in the group. Every host
    /// built by <see cref="StartAsync"/> joins the SAME literal production
    /// group (<c>"projector"</c>, this service's own
    /// <c>KafkaFactStreamSubscriber.cs:111</c>), shared sequentially across
    /// every <c>ProjectorInfraCollection</c> test — so a stale member left
    /// by one test's teardown can block the NEXT test's own host from ever
    /// being assigned a partition, exactly the mechanism the Notifications
    /// copy's own `zombieprobe` reproduced directly (a silent member held a
    /// fresh topic's partitions for the full 90s a DLQ test budgets).
    /// </summary>
    public static async Task StopHostAndWaitForGroupToClearAsync(IHost host, KafkaContainerFixture kafka, string groupId = "projector", TimeSpan? timeout = null)
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

    public static async Task PublishAsync<TPayload>(KafkaContainerFixture kafka, string topic, Envelope<TPayload> envelope)
    {
        var config = new ProducerConfig { BootstrapServers = kafka.BootstrapServers };
        using var producer = new ProducerBuilder<Null, byte[]>(config).Build();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
        await producer.ProduceAsync(topic, new Message<Null, byte[]> { Value = bytes });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    public static string TopicFor(string eventType) => eventType switch
    {
        "order.placed.v1" or "order.confirmed.v1" or "order.despatched.v1" or "order.completed.v1" or "order.cancelled.v1" or "order.saga_failed.v1" => ProjectorFactTopics.OrdersFacts,
        "stock.reserved.v1" or "stock.rejected.v1" or "stock.released.v1" => ProjectorFactTopics.FulfillmentFacts,
        "credit.approved.v1" or "credit.rejected.v1" or "credit.released.v1" or "invoice.issued.v1" or "payment.received.v1" => ProjectorFactTopics.BillingFacts,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };

    public static async Task<BsonDocument?> PollUntilAsync(
        IMongoCollection<BsonDocument> collection,
        Guid orderId,
        Func<BsonDocument, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var doc = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstOrDefaultAsync();
            if (doc is not null && predicate(doc))
            {
                return doc;
            }

            await Task.Delay(150);
        }

        return null;
    }
}
