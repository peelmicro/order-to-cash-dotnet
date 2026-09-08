using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// design.md §6.4's FIVE behavioural conformance cases, over REAL
/// <c>mongo:8.3.8</c>, with the REAL <see cref="IdempotentConsumer"/>. One
/// fixed scope document created per class, a dedup-only <c>$set</c> of the
/// key as <c>stages</c>, and a counter as the post-apply callback. <c>N10</c>:
/// asserted on the returned outcome AND a callback counter, never only on
/// the document.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class IdempotentConsumerConformanceTests(MongoContainerFixture mongoFixture)
{
    private (IMongoCollection<BsonDocument> Collection, Guid ScopeId) NewScope()
    {
        var collection = mongoFixture.FreshCollection("idempotent");
        var scopeId = Guid.NewGuid();
        collection.InsertOne(new BsonDocument
        {
            ["_id"] = scopeId.ToString("D"),
            ["processedEventKeys"] = new BsonArray(),
        });
        return (collection, scopeId);
    }

    private static BsonDocument[] DedupOnlyStages(string dedupKey) =>
    [
        new BsonDocument("$set", new BsonDocument
        {
            ["processedEventKeys"] = new BsonDocument("$setUnion", new BsonArray
            {
                new BsonDocument("$ifNull", new BsonArray { "$processedEventKeys", new BsonArray() }),
                new BsonArray { dedupKey },
            }),
        }),
    ];

    [Fact]
    public async Task PR25_Case1_AFirstCallRunsThePostApplyWorkOnceAndReportsProcessed()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var callbackCount = 0;

        var outcome = await consumer.RunOnceAsync(scopeId, Guid.NewGuid(), ConsumerName.Projector, DedupOnlyStages, (_, _) => { callbackCount++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(ConsumptionOutcome.Processed, outcome);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task PR25_Case2_ASecondCallForTheSameEventIdConsumerDoesNotRunItAndReportsDuplicate()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var eventId = Guid.NewGuid();
        var callbackCount = 0;
        Func<BsonDocument, CancellationToken, Task> callback = (_, _) => { callbackCount++; return Task.CompletedTask; };

        await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);
        var second = await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);

        Assert.Equal(ConsumptionOutcome.Duplicate, second);
        Assert.Equal(1, callbackCount); // NOT run a second time — N10: the attempt is what's counted.
    }

    [Fact]
    public async Task PR25_Case3_AConsumerConstructedFreshOverTheSameStoreStillReportsDuplicate()
    {
        var (collection, scopeId) = NewScope();
        var eventId = Guid.NewGuid();
        var callbackCount = 0;
        Func<BsonDocument, CancellationToken, Task> callback = (_, _) => { callbackCount++; return Task.CompletedTask; };

        var first = new IdempotentConsumer(collection);
        await first.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);

        // A BRAND NEW instance, over the SAME backing collection — the state
        // lives in MongoDB, not in a field of the class.
        var second = new IdempotentConsumer(collection);
        var outcome = await second.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);

        Assert.Equal(ConsumptionOutcome.Duplicate, outcome);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task PR25_Case4_TwoDistinctEventIdsBothRun()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var callbackCount = 0;
        Func<BsonDocument, CancellationToken, Task> callback = (_, _) => { callbackCount++; return Task.CompletedTask; };

        var outcome1 = await consumer.RunOnceAsync(scopeId, Guid.NewGuid(), ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);
        var outcome2 = await consumer.RunOnceAsync(scopeId, Guid.NewGuid(), ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);

        Assert.Equal(ConsumptionOutcome.Processed, outcome1);
        Assert.Equal(ConsumptionOutcome.Processed, outcome2);
        Assert.Equal(2, callbackCount);
    }

    /// <summary>Case 5 — this is WHY the key is the pair and not the bare eventId already in <c>events[]</c>.</summary>
    [Fact]
    public async Task PR25_Case5_TheSameEventIdUnderADifferentConsumerNameBothRun()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var eventId = Guid.NewGuid();
        var callbackCount = 0;
        Func<BsonDocument, CancellationToken, Task> callback = (_, _) => { callbackCount++; return Task.CompletedTask; };

        var outcome1 = await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);
        var outcome2 = await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Notifications, DedupOnlyStages, callback, CancellationToken.None);

        Assert.Equal(ConsumptionOutcome.Processed, outcome1);
        Assert.Equal(ConsumptionOutcome.Processed, outcome2);
        Assert.Equal(2, callbackCount);
    }

    [Fact]
    public async Task PR26_ThePostApplyCallbackRunsExactlyOnceOnProcessedAndNotAtAllOnDuplicate()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var eventId = Guid.NewGuid();
        var callbackCount = 0;
        Func<BsonDocument, CancellationToken, Task> callback = (_, _) => { callbackCount++; return Task.CompletedTask; };

        await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);
        Assert.Equal(1, callbackCount);

        await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, callback, CancellationToken.None);
        Assert.Equal(1, callbackCount); // unchanged — the Duplicate branch never ran it.
    }

    [Fact]
    public async Task PR23_RecordsTheConsumerEventIdPairInTheSameSingleWriteThatAppliesTheProjection()
    {
        var (collection, scopeId) = NewScope();
        var consumer = new IdempotentConsumer(collection);
        var eventId = Guid.NewGuid();

        await consumer.RunOnceAsync(scopeId, eventId, ConsumerName.Projector, DedupOnlyStages, (_, _) => Task.CompletedTask, CancellationToken.None);

        var doc = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", scopeId.ToString("D"))).FirstAsync();
        var expectedKey = $"projector:{eventId:D}";
        Assert.Contains(expectedKey, doc["processedEventKeys"].AsBsonArray.Select(v => v.AsString));
    }
}
