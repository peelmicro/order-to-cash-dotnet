using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;
using OrderToCash.Projector.Application;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.Infrastructure.Signal;
using OrderToCash.Projector.Presentation;

namespace OrderToCash.Projector.Infrastructure;

/// <summary>
/// <c>AddProjector(IServiceCollection, Action&lt;ProjectorOptions&gt;)</c>
/// — one explicit registration line per port, no assembly scan (design.md
/// §8.2). EVERYTHING is a singleton — a real difference from Notifications,
/// which holds a scoped EF Core <c>DbContext</c>: the projector holds no
/// per-request state at all, <see cref="IMongoClient"/> is thread-safe and
/// internally pooled, and the whole point of <c>PR6</c> is that no state is
/// carried between the two write operations.
/// </summary>
public static class ProjectorServiceCollectionExtensions
{
    public static IServiceCollection AddProjector(this IServiceCollection services, Action<ProjectorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ProjectorOptions();
        configure(options);

        services.AddSingleton<IOptions<ProjectorKafkaOptions>>(Options.Create(options.Kafka));

        services.AddSingleton<IMongoClient>(_ => new MongoClient(options.Mongo.ConnectionUri));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>()
            .GetDatabase(options.Mongo.Database)
            .GetCollection<BsonDocument>(ReadModelCollection.Name));

        // ONE NatsConnection instance, exposed under BOTH the concrete type
        // (ReadModelBootstrap needs ConnectAsync(), which is not a member of
        // INatsConnection) and the interface (everything else — the
        // Orders/Billing/Fulfillment registration shape).
        services.AddSingleton(_ => new NatsConnection(new NatsOpts { Url = options.Nats.Url }));
        services.AddSingleton<INatsConnection>(sp => sp.GetRequiredService<NatsConnection>());

        services.AddSingleton<IUpdateSignalPublisher, NatsUpdateSignalPublisher>();
        services.AddSingleton<IdempotentConsumer>();
        services.AddSingleton<IReadModelWriter, MongoReadModelWriter>();
        services.AddSingleton<ProjectionApplyService>();
        services.AddSingleton<IFactStreamSubscriber, KafkaFactStreamSubscriber>();

        services.AddHostedService<ReadModelBootstrap>();
        services.AddHostedService<ProjectorFactsConsumer>();

        return services;
    }
}
