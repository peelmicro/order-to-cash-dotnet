using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR7</c> — driven from SEPARATE <see cref="MongoClient"/> instances,
/// not one pool (ledger <b>L24</b>, adopting #7's reviewer's harder shape).
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class ProjectionConcurrencyTests(MongoContainerFixture mongoFixture)
{
    private const int ClientCount = 8;

    private MongoReadModelWriter NewWriterOverFreshClient(string databaseName)
    {
        var client = new MongoClient(mongoFixture.ConnectionString);
        var collection = client.GetDatabase(databaseName).GetCollection<BsonDocument>("order_timeline");
        var idempotentConsumer = new IdempotentConsumer(collection);
        return new MongoReadModelWriter(collection, idempotentConsumer);
    }

    private static ProjectionDelta ConfirmedDelta(Guid orderId, Guid eventId) =>
        new(
            orderId,
            new TimelineEntryDelta(eventId, "order.confirmed.v1", DateTimeOffset.UtcNow, "Order confirmed (ORDRSP)", null, Guid.NewGuid()),
            "confirmed", 4, null, null, null, null, null);

    /// <summary>Two deliveries of the SAME eventId, from separate clients — exactly one Processed, the rest Duplicate. Repeated ≥5 rounds.</summary>
    [Fact]
    public async Task PR7_TwoConcurrentDeliveriesOfOneEventIdFromSeparateClients_ApplyExactlyOnceAndReportTheLoserDuplicate()
    {
        for (var round = 0; round < 5; round++)
        {
            var databaseName = $"otc_rm_conc1_{Guid.NewGuid():N}"[..40];
            var orderId = Guid.NewGuid();
            var eventId = Guid.NewGuid();

            // Bring the document into existence first (both clients share the
            // SAME document — this test targets Phase 2's race, not Phase 1's).
            await NewWriterOverFreshClient(databaseName).ApplyAsync(
                new ProjectionDelta(orderId, new TimelineEntryDelta(Guid.NewGuid(), "order.placed.v1", DateTimeOffset.UtcNow.AddMinutes(-1), "seed", null, Guid.NewGuid()), "placed", 1, null, null, null, null, null),
                Guid.NewGuid(), (_, _) => Task.CompletedTask, CancellationToken.None);

            var callbackCount = 0;
            var tasks = Enumerable.Range(0, ClientCount).Select(async _ =>
            {
                var writer = NewWriterOverFreshClient(databaseName);
                return await writer.ApplyAsync(ConfirmedDelta(orderId, eventId), eventId, (_, _) => { Interlocked.Increment(ref callbackCount); return Task.CompletedTask; }, CancellationToken.None);
            });

            var outcomes = await Task.WhenAll(tasks);

            Assert.Equal(1, outcomes.Count(o => o == ProjectionOutcome.Processed));
            Assert.Equal(ClientCount - 1, outcomes.Count(o => o == ProjectionOutcome.Duplicate));
            Assert.Equal(1, callbackCount); // N10: the ATTEMPT is what's counted, not just the document.
        }
    }

    /// <summary>Two deliveries of DIFFERENT eventIds for an ABSENT order — ends with one document holding both entries, no duplicate-key error escapes.</summary>
    [Fact]
    public async Task PR7_TwoConcurrentDeliveriesOfDifferentEventIdsForAnAbsentOrder_EndWithOneDocumentHoldingBoth_NoDuplicateKeyErrorEscaping()
    {
        for (var round = 0; round < 5; round++)
        {
            var databaseName = $"otc_rm_conc2_{Guid.NewGuid():N}"[..40];
            var orderId = Guid.NewGuid();

            var callbackCount = 0;
            var tasks = Enumerable.Range(0, ClientCount).Select(async i =>
            {
                var writer = NewWriterOverFreshClient(databaseName);
                var eventId = Guid.NewGuid();
                var delta = new ProjectionDelta(
                    orderId,
                    new TimelineEntryDelta(eventId, "order.confirmed.v1", DateTimeOffset.UtcNow.AddSeconds(i), $"entry {i}", null, Guid.NewGuid()),
                    "confirmed", 4, null, null, null, null, null);
                return await writer.ApplyAsync(delta, eventId, (_, _) => { Interlocked.Increment(ref callbackCount); return Task.CompletedTask; }, CancellationToken.None);
            });

            // No exception must escape — the retry-exactly-once absorbs E11000.
            var outcomes = await Task.WhenAll(tasks);

            Assert.All(outcomes, o => Assert.Equal(ProjectionOutcome.Processed, o));
            Assert.Equal(ClientCount, callbackCount);

            var finalClient = new MongoClient(mongoFixture.ConnectionString);
            var finalCollection = finalClient.GetDatabase(databaseName).GetCollection<BsonDocument>("order_timeline");
            var count = await finalCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            Assert.Equal(1, count);

            var doc = await finalCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
            Assert.Equal(ClientCount, doc["events"].AsBsonArray.Count);
            Assert.Equal(ClientCount, doc["processedEventKeys"].AsBsonArray.Count);
        }
    }
}
