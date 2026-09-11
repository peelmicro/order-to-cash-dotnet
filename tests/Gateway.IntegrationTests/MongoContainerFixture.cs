using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>One real MongoDB — the same engine the projector's own <c>otc_read_model.order_timeline</c> collection lives in, never a mock (CLAUDE.md testing conventions).</summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.3.8").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public IMongoCollection<BsonDocument> FreshCollection(string databaseName)
    {
        var client = new MongoClient(_container.GetConnectionString());
        return client.GetDatabase(databaseName).GetCollection<BsonDocument>("order_timeline");
    }

    /// <summary>The raw connection string — <see cref="StreamProjectorEndToEndTests"/>'s own use: it needs to pass this straight into BOTH a real <c>ProjectorHost</c> and a real <c>GatewayTestHost</c>'s own <c>options.Mongo.ConnectionUri</c>, not merely open a collection through it directly.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Review round 2, D7 — R60/OR6, design.md §8.3: a REAL <c>docker
    /// pause</c>, never a faked failure. The SAME wrapper
    /// <c>Projector.IntegrationTests/TestSupport/MongoContainerFixture.cs</c>
    /// already carries — used by <c>HealthProbesTests</c> only; every other
    /// test in this SHARED collection relies on the caller always
    /// unpausing (a <c>try</c>/<c>finally</c>) before its own test method
    /// returns.
    /// </summary>
    public Task PauseAsync() => _container.PauseAsync();

    public Task UnpauseAsync() => _container.UnpauseAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MongoCollection : ICollectionFixture<MongoContainerFixture>
{
    public const string Name = "GatewayMongo";
}
