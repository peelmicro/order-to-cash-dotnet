// VARIANT OF — src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs
//
// Divergence: the ledger is not a `processed_events` row inside a SQL
// transaction; it is the `processedEventKeys` array of the read-model
// document itself, written by the SAME single findAndModify that applies the
// projection. There is no transaction to share, because there is nothing to
// keep consistent — the mark and the effect are the same bytes in the same
// write (design.md §6.3).
//
// Behavioural conformance: tests/Projector.IntegrationTests/IdempotentConsumerConformanceTests.cs
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Persistence;

namespace OrderToCash.Projector.Infrastructure.Messaging;

/// <summary>Whether this delivery ran the work's effects, or was already recorded and did nothing.</summary>
public enum ConsumptionOutcome
{
    Processed,
    Duplicate,
}

/// <summary>
/// Runs the caller-supplied projection pipeline AT MOST ONCE for
/// (<paramref name="scopeId"/>'s document, <c>eventId</c>, <c>consumer</c>)
/// — <c>R17</c>, <c>R18</c>, design.md §6.1/§6.2. The dedup key is the PAIR
/// <c>&lt;consumer&gt;:&lt;eventId&gt;</c>, never the bare id already
/// sitting in <c>events[]</c> (conformance case 5). The filter <c>{ _id:
/// scopeId, processedEventKeys: { $ne: dedupKey } }</c> IS the idempotency
/// check — there is no <c>Find</c>-then-write anywhere in this class, which
/// is what makes the check atomic with the effect: they are the SAME single
/// <c>findAndModify</c>. Assumes the scope document already exists — the
/// caller is responsible for bringing it into existence first (the
/// projector's own <c>PR8</c> placeholder upsert).
/// </summary>
public sealed class IdempotentConsumer(IMongoCollection<BsonDocument> collection)
{
    public async Task<ConsumptionOutcome> RunOnceAsync(
        Guid scopeId,
        Guid eventId,
        ConsumerName consumer,
        Func<string, BsonDocument[]> stages,
        Func<BsonDocument, CancellationToken, Task> afterApplied,
        CancellationToken cancellationToken)
    {
        var dedupKey = $"{ConsumerNames.ToToken(consumer)}:{eventId:D}";

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq(ReadModelCollection.Fields.Id, scopeId.ToString("D")),
            Builders<BsonDocument>.Filter.Ne(ReadModelCollection.Fields.ProcessedEventKeys, dedupKey));

        PipelineDefinition<BsonDocument, BsonDocument> pipeline = stages(dedupKey);

        var applied = await collection.FindOneAndUpdateAsync(
            filter,
            Builders<BsonDocument>.Update.Pipeline(pipeline),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);

        if (applied is null)
        {
            // The filter did not match — either this (eventId, consumer)
            // pair was already recorded, or scopeId's document does not
            // exist at all. The caller is responsible for the latter never
            // happening (Phase 1's placeholder upsert).
            return ConsumptionOutcome.Duplicate;
        }

        await afterApplied(applied, cancellationToken).ConfigureAwait(false);
        return ConsumptionOutcome.Processed;
    }
}
