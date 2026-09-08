using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

[Collection(ProjectorInfraCollection.Name)]
public sealed class ReadModelBootstrapTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    /// <summary><c>PR43</c> — <c>StartAsync</c> returns only AFTER the indexes and the migration exist.</summary>
    [Fact]
    public async Task PR43_StartAsyncReturnsOnlyAfterTheIndexesAndTheMigrationExist_AndAThrowingBootstrapFailsTheHost()
    {
        var collection = mongoFixture.FreshCollection("bootstrap-ordering");

        // A version-1 document, so the migration has genuine work to do.
        await collection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = Guid.NewGuid().ToString("D"),
            ["orderId"] = Guid.NewGuid().ToString("D"),
            ["status"] = "placed",
            ["events"] = new BsonArray(),
            ["headerComplete"] = false,
            ["updatedAt"] = "2026-01-01T00:00:00.000Z",
            ["statusRank"] = 1,
            ["timelineOrderVersion"] = 1,
            ["processedEventKeys"] = new BsonArray(),
        });

        var natsConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        var bootstrap = new ReadModelBootstrap(collection, natsConnection, NullLogger<ReadModelBootstrap>.Instance);

        await bootstrap.StartAsync(CancellationToken.None);

        // By the time StartAsync RETURNS, both must already be true.
        var indexNames = (await (await collection.Indexes.ListAsync()).ToListAsync()).Select(i => i["name"].AsString).ToList();
        Assert.Contains(ReadModelIndexes.OrderReferenceIndexName, indexNames);
        Assert.Contains(ReadModelIndexes.StatusUpdatedAtIndexName, indexNames);

        var migrated = await collection.Find(FilterDefinition<BsonDocument>.Empty).FirstAsync();
        Assert.Equal(2, migrated["timelineOrderVersion"].AsInt32);

        await natsConnection.DisposeAsync();
    }

    [Fact]
    public async Task AThrowingBootstrapFailsTheHost()
    {
        // A NON-partial index of the SAME name — forces PR22's own throw.
        var collection = mongoFixture.FreshCollection("bootstrap-throws");
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(ReadModelCollection.Fields.OrderReference),
            new CreateIndexOptions { Unique = true, Name = ReadModelIndexes.OrderReferenceIndexName }));

        var natsConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        var bootstrap = new ReadModelBootstrap(collection, natsConnection, NullLogger<ReadModelBootstrap>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.StartAsync(CancellationToken.None));

        await natsConnection.DisposeAsync();
    }
}
