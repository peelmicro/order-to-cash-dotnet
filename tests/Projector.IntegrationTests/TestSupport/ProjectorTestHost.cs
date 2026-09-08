using System.Text.Json;
using Confluent.Kafka;
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
