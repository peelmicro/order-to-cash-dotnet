using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <c>PR43</c>'s boot contract — runs index creation (<c>PR22</c>), then the
/// timeline-order migration (<c>PR32</c>), then (<c>PR45</c>, gate row 1,
/// approved 2026-09-07) connects the shared NATS singleton to completion,
/// ALL before the fact consumer's first poll. An <see cref="IHostedService"/>
/// DIRECTLY, deliberately NOT a <see cref="BackgroundService"/> — a
/// <see cref="BackgroundService"/>'s <c>StartAsync</c> returns at the first
/// <see langword="await"/> inside <c>ExecuteAsync</c>, which would give the
/// APPEARANCE of ordering with none of the substance: the host would
/// consider this service "started" the moment <c>ExecuteAsync</c> yields,
/// long before the indexes or the migration have actually completed. A
/// throw here fails the host.
/// </summary>
public sealed class ReadModelBootstrap(
    IMongoCollection<BsonDocument> collection,
    NatsConnection natsConnection,
    ILogger<ReadModelBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReadModelIndexes.EnsureAsync(collection, cancellationToken).ConfigureAwait(false);

        var migration = await TimelineOrderMigration.RunAsync(collection, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Timeline-order migration: {Migrated} document(s) re-sorted to the current version, " +
            "{MissingCausationId} of them still holding at least one entry with no causationId.",
            migration.DocumentsMigrated,
            migration.DocumentsWithAnEntryMissingCausationId);

        // PR45 (gate row 1, approved): connect EAGERLY here, not lazily on
        // first publish. NatsConnection.ConnectAsync() connects lazily by
        // default — the other three NATS services in this repository never
        // noticed because they are RESPONDERS whose SubscribeAsync
        // materialises the connection at startup. The projector only
        // publishes, and PR19 correctly logs-and-swallows a TRANSIENT
        // publish failure — composed with lazy connect, a misconfigured
        // NATS_URL would otherwise produce a projector that projects
        // perfectly and signals nothing, forever, with every test on a good
        // URL passing. A throw here fails the host, exactly as a bad
        // NATS_URL failed #7's boot (`await connect(...)` in main.ts).
        await natsConnection.ConnectAsync().ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
