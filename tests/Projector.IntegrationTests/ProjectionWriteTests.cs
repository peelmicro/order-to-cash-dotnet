using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using OrderToCash.Contracts.Wire;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

[Collection(ProjectorInfraCollection.Name)]
public sealed class ProjectionWriteTests(MongoContainerFixture mongoFixture)
{
    private static ProjectionDelta ConfirmedDelta(Guid orderId, Guid eventId, DateTimeOffset occurredAt) =>
        new(
            orderId,
            new TimelineEntryDelta(eventId, "order.confirmed.v1", occurredAt, "Order confirmed (ORDRSP)", null, Guid.NewGuid()),
            "confirmed", 4, null, null, null, null, null);

    /// <summary><c>PR6</c>: exactly one <c>update</c> and one <c>findAndModify</c>, no <c>find</c>, no <c>aggregate</c> — proven by command monitoring, the only way to prove the absence of a read. Arm by inserting a FindAsync before the apply.</summary>
    [Fact]
    public async Task PR6_AppliesOneFactInExactlyOneUpsertAndOneFilteredFindAndModify_IssuingNoReadOfOrderTimeline()
    {
        var database = mongoFixture.FreshDatabase("write-commands");
        var commandNames = new List<string>();

        var settings = MongoClientSettings.FromConnectionString(mongoFixture.ConnectionString);
        settings.ClusterConfigurator = cb =>
        {
            cb.Subscribe<CommandStartedEvent>(e =>
            {
                if (e.CommandName is "update" or "findAndModify" or "find" or "aggregate" or "count")
                {
                    lock (commandNames)
                    {
                        commandNames.Add(e.CommandName);
                    }
                }
            });
        };

        var client = new MongoClient(settings);
        var collection = client.GetDatabase(database.DatabaseNamespace.DatabaseName).GetCollection<BsonDocument>("order_timeline");
        var idempotentConsumer = new IdempotentConsumer(collection);
        var writer = new MongoReadModelWriter(collection, idempotentConsumer);

        var orderId = Guid.NewGuid();
        var delta = ConfirmedDelta(orderId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var outcome = await writer.ApplyAsync(delta, delta.Entry.EventId, (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Processed, outcome);
        Assert.Equal(["update", "findAndModify"], commandNames);

        // Redelivery of the SAME eventId — still exactly update + findAndModify, no read.
        commandNames.Clear();
        var duplicateOutcome = await writer.ApplyAsync(delta, delta.Entry.EventId, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(ProjectionOutcome.Duplicate, duplicateOutcome);
        Assert.Equal(["update", "findAndModify"], commandNames);
    }

    [Fact]
    public async Task PR41_AStoredDocumentDeserialisesThroughTheSeedsOrderTimelineDocumentClassMapWithNoLoss()
    {
        var collection = mongoFixture.FreshCollection("write-deserialise");
        var idempotentConsumer = new IdempotentConsumer(collection);
        var writer = new MongoReadModelWriter(collection, idempotentConsumer);

        var header = new OrderHeaderDelta(
            "ORD-000001", DateTimeOffset.UtcNow, "RET01", "buyer-gln", "COM01", "supplier-gln", "EUR",
            1000, 0, 1000, [new OrderItemDelta("SKU1", 2, 500, 0)]);
        var orderId = Guid.NewGuid();
        var placedDelta = new ProjectionDelta(
            orderId,
            new TimelineEntryDelta(Guid.NewGuid(), "order.placed.v1", DateTimeOffset.UtcNow, "Order ORD-000001 placed for RET01", null, Guid.NewGuid()),
            "placed", 1, null, null, null, null, header);

        await writer.ApplyAsync(placedDelta, placedDelta.Entry.EventId, (_, _) => Task.CompletedTask, CancellationToken.None);

        var typedCollection = collection.Database.GetCollection<OrderTimelineDocument>(collection.CollectionNamespace.CollectionName);
        var typed = await typedCollection.Find(Builders<OrderTimelineDocument>.Filter.Eq(d => d.Id, orderId.ToString("D"))).FirstAsync();

        Assert.Equal("ORD-000001", typed.OrderReference);
        Assert.Equal("RET01", typed.Retailer!.Code);
        Assert.Equal(1000, typed.Totals!.InitialAmount);
        Assert.Single(typed.Events);
        Assert.Equal("order.placed.v1", typed.Events[0].EventType);

        // Element-name-sequence order proof, top level.
        var raw = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
        var expectedOrder = new[]
        {
            "_id", "orderId", "orderReference", "orderDate", "retailer", "company", "status",
            "cancellationReason", "currency", "totals", "items", "references", "events",
            "headerComplete", "updatedAt", "statusRank", "timelineOrderVersion", "processedEventKeys",
        };
        Assert.Equal(expectedOrder, raw.Names);
    }

    [Fact]
    public async Task PR33_TheStoredEntryCarriesCausationId_AndTheDocumentCarriesTheThreeInternalFields()
    {
        var collection = mongoFixture.FreshCollection("write-causation");
        var idempotentConsumer = new IdempotentConsumer(collection);
        var writer = new MongoReadModelWriter(collection, idempotentConsumer);

        var causationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var delta = new ProjectionDelta(
            orderId,
            new TimelineEntryDelta(Guid.NewGuid(), "order.confirmed.v1", DateTimeOffset.UtcNow, "Order confirmed (ORDRSP)", null, causationId),
            "confirmed", 4, null, null, null, null, null);

        await writer.ApplyAsync(delta, delta.Entry.EventId, (_, _) => Task.CompletedTask, CancellationToken.None);

        var doc = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
        var entry = doc["events"].AsBsonArray[0].AsBsonDocument;
        Assert.Equal(causationId.ToString("D"), entry["causationId"].AsString);

        Assert.True(doc.Contains("statusRank"));
        Assert.True(doc.Contains("processedEventKeys"));
        Assert.True(doc.Contains("timelineOrderVersion"));
    }
}
