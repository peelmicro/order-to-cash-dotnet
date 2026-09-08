using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>
/// One real MongoDB container — <c>mongo:8.3.8</c>, the same tag
/// <c>tests/Seed.IntegrationTests/SeedContainersFixture.cs</c> and
/// <c>docker-compose.infra.yml</c> use, a STANDALONE (no replica set — L14,
/// L22).
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.3.8").Build();

    public async Task InitializeAsync() => await _mongo.StartAsync();

    public async Task DisposeAsync() => await _mongo.DisposeAsync();

    /// <summary>A fresh, uniquely-named database + its <c>order_timeline</c> collection. MongoDB caps database names at 63 characters, so the name is kept short: an 8-character slug plus an 8-character random suffix.</summary>
    public IMongoCollection<MongoDB.Bson.BsonDocument> FreshCollection(string databaseNameSuffix)
    {
        var client = new MongoClient(_mongo.GetConnectionString());
        var database = client.GetDatabase(ShortDatabaseName(databaseNameSuffix));
        return database.GetCollection<MongoDB.Bson.BsonDocument>("order_timeline");
    }

    public IMongoDatabase FreshDatabase(string databaseNameSuffix)
    {
        var client = new MongoClient(_mongo.GetConnectionString());
        return client.GetDatabase(ShortDatabaseName(databaseNameSuffix));
    }

    private static string ShortDatabaseName(string suffix)
    {
        var slug = suffix.Length > 12 ? suffix[..12] : suffix;
        return $"otc_rm_{slug}_{Guid.NewGuid():N}"; // at most 7 + 12 + 1 + 32 = 52 characters, well under MongoDB's 63-character database-name limit.
    }

    public string ConnectionString => _mongo.GetConnectionString();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProjectorInfraCollection :
    ICollectionFixture<MongoContainerFixture>,
    ICollectionFixture<KafkaContainerFixture>,
    ICollectionFixture<NatsContainerFixture>
{
    public const string Name = "ProjectorInfra";
}
