using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>Boot log figures for <see cref="TimelineOrderMigration.RunAsync"/> — <c>PR35</c>: never invented, only reported.</summary>
public sealed record TimelineOrderMigrationResult(long DocumentsMigrated, long DocumentsWithAnEntryMissingCausationId);

/// <summary>
/// <c>PR32</c> — brings every stored document's <c>events</c> order to the
/// CURRENT timeline-order version before the fact consumer's first poll,
/// selecting documents by <c>{ timelineOrderVersion: { $ne: current } }</c>
/// — NEVER by the presence or absence of a field, which is the rejected
/// shape (<c>PR29</c>'s legacy-document backfill does not apply here; §2.10).
/// Re-sorts with the SAME <see cref="TimelineOrder"/> expression the live
/// apply uses, stamps the version, and touches no other client-visible
/// field. <c>PR35</c>: never invents a <c>causationId</c> for an entry that
/// has none.
/// </summary>
public static class TimelineOrderMigration
{
    private const int CurrentVersion = 2;
    private const int MaxEntriesPerOrder = 32;

    public static async Task<TimelineOrderMigrationResult> RunAsync(IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        var versionFilter = Builders<BsonDocument>.Filter.Ne(ReadModelCollection.Fields.TimelineOrderVersion, CurrentVersion);

        var missingCausationIdFilter = Builders<BsonDocument>.Filter.And(
            versionFilter,
            new BsonDocument($"{ReadModelCollection.Fields.Events}.{ReadModelCollection.Fields.Event.CausationId}", new BsonDocument("$exists", false)));

        var missingCausationIdCount = await collection.CountDocumentsAsync(missingCausationIdFilter, cancellationToken: cancellationToken).ConfigureAwait(false);

        var setArguments = new BsonDocument
        {
            [ReadModelCollection.Fields.Events] = TimelineOrder.Expression($"${ReadModelCollection.Fields.Events}", MaxEntriesPerOrder),
            [ReadModelCollection.Fields.TimelineOrderVersion] = CurrentVersion,
        };

        // ONE pipeline stage — a $set — wrapping the field map above. Every
        // aggregation-pipeline stage document must have EXACTLY ONE
        // top-level field (its operator); setArguments is $set's own
        // argument, never top-level pipeline-stage fields themselves (the
        // same mistake, and the same fix, as DeltaToPipeline.For).
        PipelineDefinition<BsonDocument, BsonDocument> pipeline = new BsonDocument[] { new("$set", setArguments) };

        var result = await collection.UpdateManyAsync(
            versionFilter,
            Builders<BsonDocument>.Update.Pipeline(pipeline),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new TimelineOrderMigrationResult(result.ModifiedCount, missingCausationIdCount);
    }
}
