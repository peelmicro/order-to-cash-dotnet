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
    /// <summary>The one literal production group every host in this project joins (<c>KafkaFactStreamSubscriber</c>).</summary>
    public const string KafkaGroupId = "projector";

    public static async Task<KafkaGroupTestHost> StartAsync(
        KafkaContainerFixture kafka,
        NatsContainerFixture nats,
        string mongoConnectionUri,
        string mongoDatabase,
        Action<OrderToCash.Projector.Infrastructure.ProjectorOptions>? configure = null)
    {
        // Backlog id 74 bullet 6 — the ENFORCEMENT half, on the setup path.
        // A no-op unless a previous teardown recorded a leak on this group;
        // deliberately here rather than in a `finally`, where a throw would
        // replace the failing test's own exception.
        await KafkaGroupClearance.EnsureGroupIsClearBeforeStartAsync(kafka.BootstrapServers, KafkaGroupId);

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

        // Backlog id 74 bullet 6 — every host this helper hands out is a
        // KafkaGroupTestHost, so a bare host.StopAsync() STILL clears the
        // group. The escape advisory A16 named does not exist for these hosts.
        var host = new KafkaGroupTestHost(builder.Build(), kafka.BootstrapServers, KafkaGroupId);
        await host.StartAsync();
        return host;
    }

    /// <remarks>
    /// Backlog id 74 bullet 6 — the body moved to
    /// <see cref="KafkaGroupClearance"/> and the wait NO LONGER THROWS. The
    /// existing call sites are all in <c>finally</c> blocks, where a throw
    /// REPLACES the exception the test itself is reporting; enforcement moved
    /// to <see cref="KafkaGroupClearance.EnsureGroupIsClearBeforeStartAsync"/>
    /// on the next host's setup path. Kept as a method so no call site had to
    /// change, and idempotent against <see cref="KafkaGroupTestHost"/>'s own
    /// teardown.
    /// </remarks>
    public static Task StopHostAndWaitForGroupToClearAsync(IHost host, KafkaContainerFixture kafka, string groupId = KafkaGroupId, TimeSpan? timeout = null) =>
        host is KafkaGroupTestHost wrapper
            ? wrapper.StopAndClearGroupAsync(timeout)
            : KafkaGroupClearance.StopAndClearAsync(host, kafka.BootstrapServers, groupId, timeout);

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
