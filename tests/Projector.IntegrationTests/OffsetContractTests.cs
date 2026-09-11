using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR38</c>/<c>PR5</c> — each test owns a PRIVATE Kafka/NATS/Mongo
/// stack rather than the shared collection: an offset-contract assertion is
/// only meaningful against a group whose starting state THIS test controls,
/// and <c>PR5</c> specifically needs a group that has genuinely never
/// subscribed before — the shared collection's "projector" group persists
/// across every other test file in this project.
/// </summary>
public sealed class OffsetContractTests : IAsyncLifetime
{
    private readonly KafkaContainerFixture _kafka = new();
    private readonly NatsContainerFixture _nats = new();
    private readonly MongoContainerFixture _mongo = new();

    public async Task InitializeAsync()
    {
        await _kafka.InitializeAsync();
        await _nats.InitializeAsync();
        await _mongo.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _kafka.DisposeAsync();
        await _nats.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    private const string GroupId = "projector";
    private const int PartitionCount = 6;

    private static async Task<(long Total, string Description)> ReadCommittedOffsetsAsync(string bootstrapServers, string topic, TimeSpan requestTimeout) =>
        await Task.Run(() =>
        {
            var config = new ConsumerConfig { BootstrapServers = bootstrapServers, GroupId = GroupId, EnableAutoCommit = false };
            using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
            var partitions = Enumerable.Range(0, PartitionCount).Select(p => new TopicPartition(topic, new Partition(p))).ToList();

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

    /// <summary><c>PR38</c>: a throwing handler leaves the committed offset UNCHANGED, read from the broker — never inferred from the fact that a redelivery happened.</summary>
    [Fact]
    public async Task PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker()
    {
        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = _kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Nats.Url = _nats.Url;
                options.Mongo.ConnectionUri = _mongo.ConnectionString;
                options.Mongo.Database = "otc_rm_offset_throw";
            });

        var gate = new ThrowOnceGate();
        builder.Services.Replace(ServiceDescriptor.Singleton<IReadModelWriter>(sp =>
            new ThrowOnceReadModelWriter(new OrderToCash.Projector.Infrastructure.Persistence.MongoReadModelWriter(
                sp.GetRequiredService<IMongoCollection<BsonDocument>>(),
                sp.GetRequiredService<OrderToCash.Projector.Infrastructure.Messaging.IdempotentConsumer>()),
                gate)));

        var host = builder.Build();
        await host.StartAsync();
        try
        {
            var (baseline, baselineDescription) = await ReadCommittedOffsetsAsync(_kafka.BootstrapServers, ProjectorFactTopics.OrdersFacts, TimeSpan.FromSeconds(10));

            var envelope = EnvelopeBuilders.OrderPlaced();
            await ProjectorTestHost.PublishAsync(_kafka, ProjectorFactTopics.OrdersFacts, envelope);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && gate.Attempts < 1)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 1, "the decorated writer was never reached — the fact never arrived at all.");

            var redeliveryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < redeliveryDeadline && gate.Attempts < 2)
            {
                await Task.Delay(100);
            }

            Assert.True(gate.Attempts >= 2, "the redelivery never reached the decorated writer a second time within the wait budget.");

            var (afterFailedDelivery, afterFailedDescription) = await ReadCommittedOffsetsAsync(_kafka.BootstrapServers, ProjectorFactTopics.OrdersFacts, TimeSpan.FromSeconds(10));
            Assert.True(baseline == afterFailedDelivery, $"the committed offset moved before the redelivery succeeded — baseline=[{baselineDescription}] afterFailedDelivery=[{afterFailedDescription}].");

            gate.Release();

            var mongoClient = new MongoClient(_mongo.ConnectionString);
            var collection = mongoClient.GetDatabase("otc_rm_offset_throw").GetCollection<BsonDocument>("order_timeline");
            var doc = await ProjectorTestHost.PollUntilAsync(collection, envelope.CorrelationId, _ => true, TimeSpan.FromSeconds(15));
            Assert.NotNull(doc);
        }
        // Mechanism-2 classification: does NOT need the group-clearance
        // wait — this test class owns a PRIVATE, per-test-method
        // KafkaContainerFixture (`_kafka = new()`, no [Collection]
        // sharing; xUnit builds a fresh class instance per [Fact], so
        // InitializeAsync spins up a brand-new broker each time) never
        // touched by any other test — mechanism 2 needs a broker SHARED
        // across tests to cross a test boundary, which cannot happen here.
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary><c>PR5</c>: a fact produced BEFORE the projector group ever subscribed is still consumed — the only condition under which Earliest and Latest differ.</summary>
    [Fact]
    public async Task PR5_AFactProducedBeforeTheProjectorGroupEverSubscribedIsStillConsumed()
    {
        var envelope = EnvelopeBuilders.OrderPlaced();

        // Produce FIRST — the "projector" group in THIS private broker has never subscribed.
        await ProjectorTestHost.PublishAsync(_kafka, ProjectorFactTopics.OrdersFacts, envelope);

        var host = await ProjectorTestHost.StartAsync(_kafka, _nats, _mongo.ConnectionString, "otc_rm_offset_pr5");
        try
        {
            var mongoClient = new MongoClient(_mongo.ConnectionString);
            var collection = mongoClient.GetDatabase("otc_rm_offset_pr5").GetCollection<BsonDocument>("order_timeline");

            var doc = await ProjectorTestHost.PollUntilAsync(collection, envelope.CorrelationId, d => d["events"].AsBsonArray.Count == 1, TimeSpan.FromSeconds(20));
            Assert.NotNull(doc);
            Assert.Equal("order.placed.v1", doc!["events"].AsBsonArray[0]["eventType"].AsString);
        }
        // Mechanism-2 classification: does NOT need the group-clearance
        // wait — this test class owns a PRIVATE, per-test-method
        // KafkaContainerFixture (`_kafka = new()`, no [Collection]
        // sharing; xUnit builds a fresh class instance per [Fact], so
        // InitializeAsync spins up a brand-new broker each time) never
        // touched by any other test — mechanism 2 needs a broker SHARED
        // across tests to cross a test boundary, which cannot happen here.
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class ThrowOnceGate
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }

        public async Task BeforeApplyAsync()
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new InvalidOperationException("simulated failure — first delivery only");
            }

            await _release.Task;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class ThrowOnceReadModelWriter(IReadModelWriter inner, ThrowOnceGate gate) : IReadModelWriter
    {
        public async Task<ProjectionOutcome> ApplyAsync(ProjectionDelta delta, Guid eventId, Func<ReadModelDocument, CancellationToken, Task> afterApplied, CancellationToken cancellationToken)
        {
            await gate.BeforeApplyAsync();
            return await inner.ApplyAsync(delta, eventId, afterApplied, cancellationToken);
        }
    }
}
