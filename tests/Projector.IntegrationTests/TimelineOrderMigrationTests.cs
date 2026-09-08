using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

[Collection(ProjectorInfraCollection.Name)]
public sealed class TimelineOrderMigrationTests(MongoContainerFixture mongoFixture)
{
    private static BsonDocument Entry(string eventId, string occurredAt, string? causationId = null)
    {
        var doc = new BsonDocument
        {
            ["eventId"] = eventId,
            ["eventType"] = "order.confirmed.v1",
            ["occurredAt"] = occurredAt,
            ["summary"] = "x",
        };
        if (causationId is not null)
        {
            doc["causationId"] = causationId;
        }

        return doc;
    }

    private static BsonDocument DocumentAtVersion(int version, BsonArray events) => new()
    {
        ["_id"] = Guid.NewGuid().ToString("D"),
        ["orderId"] = Guid.NewGuid().ToString("D"),
        ["status"] = "confirmed",
        ["events"] = events,
        ["headerComplete"] = false,
        ["updatedAt"] = "2026-01-01T00:00:00.000Z",
        ["statusRank"] = 4,
        ["timelineOrderVersion"] = version,
        ["processedEventKeys"] = new BsonArray(),
    };

    [Fact]
    public async Task PR32_SelectsOnTheVersionStamp_ADocumentStampedAtCurrentMinusOneIsReSorted()
    {
        var collection = mongoFixture.FreshCollection("migration-v1");

        // Wrong causal order in the raw array: c depends on a (same occurredAt),
        // but c is listed BEFORE a.
        var idA = "11111111-1111-1111-1111-111111111111";
        var idC = "33333333-3333-3333-3333-333333333333";
        var events = new BsonArray
        {
            Entry(idC, "2026-01-01T00:00:00.000Z", causationId: idA),
            Entry(idA, "2026-01-01T00:00:00.000Z"),
        };
        var doc = DocumentAtVersion(1, events);
        await collection.InsertOneAsync(doc);

        var result = await TimelineOrderMigration.RunAsync(collection, CancellationToken.None);

        Assert.Equal(1, result.DocumentsMigrated);

        var migrated = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"])).FirstAsync();
        Assert.Equal(2, migrated["timelineOrderVersion"].AsInt32);
        var reSorted = migrated["events"].AsBsonArray;
        Assert.Equal(idA, reSorted[0]["eventId"].AsString); // cause first
        Assert.Equal(idC, reSorted[1]["eventId"].AsString); // effect second
    }

    [Fact]
    public async Task PR32_IsANoOpOnADocumentAlreadyAtTheCurrentVersion()
    {
        var collection = mongoFixture.FreshCollection("migration-noop");
        var events = new BsonArray { Entry("44444444-4444-4444-4444-444444444444", "2026-01-01T00:00:00.000Z") };
        var doc = DocumentAtVersion(2, events);
        await collection.InsertOneAsync(doc);

        var before = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"])).FirstAsync();

        var result = await TimelineOrderMigration.RunAsync(collection, CancellationToken.None);

        Assert.Equal(0, result.DocumentsMigrated);

        var after = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"])).FirstAsync();
        Assert.Equal(before, after); // byte-identical.
    }

    [Fact]
    public async Task PR35_NeverInventsACausationId_AndReportsBothCounts()
    {
        var collection = mongoFixture.FreshCollection("migration-nocausation");

        // One entry with no causationId at all.
        var events = new BsonArray { Entry("55555555-5555-5555-5555-555555555555", "2026-01-01T00:00:00.000Z", causationId: null) };
        var doc = DocumentAtVersion(1, events);
        await collection.InsertOneAsync(doc);

        var result = await TimelineOrderMigration.RunAsync(collection, CancellationToken.None);

        Assert.Equal(1, result.DocumentsMigrated);
        Assert.Equal(1, result.DocumentsWithAnEntryMissingCausationId);

        var migrated = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"])).FirstAsync();
        Assert.False(migrated["events"].AsBsonArray[0].AsBsonDocument.Contains("causationId")); // never invented.
    }

    /// <summary>Arm by changing the filter to <c>{ timelineOrderVersion: { $exists: false } }</c> — the rejected shape — confirming the version-stamped case is missed.</summary>
    [Fact]
    public async Task TheFilterIsTheVersionStamp_NeverPresenceOrAbsenceOfAField()
    {
        var collection = mongoFixture.FreshCollection("migration-filtershape");
        var events = new BsonArray { Entry("66666666-6666-6666-6666-666666666666", "2026-01-01T00:00:00.000Z") };
        var doc = DocumentAtVersion(1, events); // HAS the field, just at the wrong value.
        await collection.InsertOneAsync(doc);

        var wrongFilter = Builders<BsonDocument>.Filter.Exists("timelineOrderVersion", false);
        var matchCount = await collection.CountDocumentsAsync(wrongFilter);
        Assert.Equal(0, matchCount); // the document HAS the field — an $exists:false filter would miss it entirely.

        var result = await TimelineOrderMigration.RunAsync(collection, CancellationToken.None);
        Assert.Equal(1, result.DocumentsMigrated); // the real (Ne-based) filter DOES catch it.
    }
}
