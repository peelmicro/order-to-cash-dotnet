using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;
using OrderToCash.Projector.Application;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.Infrastructure.Signal;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>The real projection pipeline — <see cref="ProjectionApplyService"/> over a real Mongo collection and a real NATS connection, without going through Kafka.</summary>
public sealed class ProjectionRuntime : IAsyncDisposable
{
    public IMongoCollection<BsonDocument> Collection { get; }

    public NatsConnection NatsConnection { get; }

    public ProjectionApplyService ApplyService { get; }

    public int PublishFailureCount { get; private set; }

    private ProjectionRuntime(IMongoCollection<BsonDocument> collection, NatsConnection natsConnection, ProjectionApplyService applyService)
    {
        Collection = collection;
        NatsConnection = natsConnection;
        ApplyService = applyService;
    }

    public static async Task<ProjectionRuntime> CreateAsync(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture, string suffix)
    {
        var collection = mongoFixture.FreshCollection(suffix);
        var natsConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await natsConnection.ConnectAsync();

        var idempotentConsumer = new IdempotentConsumer(collection);
        var writer = new MongoReadModelWriter(collection, idempotentConsumer);
        var publisher = new NatsUpdateSignalPublisher(natsConnection);
        var applyService = new ProjectionApplyService(writer, publisher, NullLogger<ProjectionApplyService>.Instance);

        return new ProjectionRuntime(collection, natsConnection, applyService);
    }

    public Task ApplyAsync(FactEnvelope envelope, CancellationToken cancellationToken = default) =>
        ApplyService.ApplyAsync(envelope, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await NatsConnection.DisposeAsync();
    }
}
