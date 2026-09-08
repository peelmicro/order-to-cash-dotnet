using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <see cref="IReadModelWriter"/> over the two-operation variant
/// (design.md §5.1, §5.2). Phase 1 (bring the document into existence) is
/// owned HERE, including the <c>E11000</c> retry-exactly-once (<c>PR7</c>) —
/// it lives here and ONLY here; every other error propagates so the fact is
/// redelivered. Phase 2 (the atomic, filtered apply) is delegated to
/// <see cref="IdempotentConsumer"/>, the documented MongoDB rendering of the
/// idempotent-consumer pattern (<c>PR23</c>).
/// </summary>
public sealed class MongoReadModelWriter(IMongoCollection<BsonDocument> collection, IdempotentConsumer idempotentConsumer) : IReadModelWriter
{
    public async Task<ProjectionOutcome> ApplyAsync(
        ProjectionDelta delta,
        Guid eventId,
        Func<ReadModelDocument, CancellationToken, Task> afterApplied,
        CancellationToken cancellationToken)
    {
        var occurredAtWire = InstantWire.Of(delta.Entry.OccurredAt);

        await UpsertPlaceholderAsync(delta.OrderId, occurredAtWire, cancellationToken).ConfigureAwait(false);

        var outcome = await idempotentConsumer.RunOnceAsync(
            scopeId: delta.OrderId,
            eventId,
            Application.Ports.ConsumerName.Projector,
            stages: dedupKey => DeltaToPipeline.For(delta, dedupKey),
            afterApplied: (applied, ct) => afterApplied(ToReadModelDocument(delta, applied), ct),
            cancellationToken).ConfigureAwait(false);

        return outcome == ConsumptionOutcome.Processed ? ProjectionOutcome.Processed : ProjectionOutcome.Duplicate;
    }

    /// <summary>
    /// Phase 1 — <c>PR6</c>/<c>PR8</c>: <c>$setOnInsert</c> is a no-op by
    /// construction against an existing document, so this never touches
    /// <c>events</c>/<c>status</c>/<c>references</c>/<c>processedEventKeys</c>
    /// of a pre-existing document. Its one race is the classic upsert race
    /// (<c>PR7</c>): two concurrent deliveries for an ABSENT order both fail
    /// to match and both attempt an insert; the loser raises <c>E11000</c>
    /// on <c>_id</c>, retried here EXACTLY ONCE, after which it can only
    /// match. Not delegated to the driver's retryable writes: they require a
    /// replica set, and the compose stack's <c>mongo:8.3.8</c> is a
    /// standalone (ledger <b>L22</b>).
    /// </summary>
    private async Task UpsertPlaceholderAsync(Guid orderId, string occurredAtWire, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(ReadModelCollection.Fields.Id, orderId.ToString("D"));

        // A PIPELINE update, not the classic $setOnInsert operator — see
        // PlaceholderDocument.UpsertPipeline's own remarks: $setOnInsert
        // does not preserve the placeholder's field order on insert.
        PipelineDefinition<BsonDocument, BsonDocument> pipeline = PlaceholderDocument.UpsertPipeline(orderId, occurredAtWire);
        UpdateDefinition<BsonDocument> update = Builders<BsonDocument>.Update.Pipeline(pipeline);
        var options = new UpdateOptions { IsUpsert = true };

        try
        {
            await collection.UpdateOneAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The loser of the upsert race — retry EXACTLY once, after
            // which the document exists and the update can only match.
            await collection.UpdateOneAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the application-facing description of the post-apply state
    /// (<c>PR42</c>): the timeline-entry fields come verbatim from the
    /// fact just applied (they are, by construction, exactly what the
    /// atomic apply appended — a redelivery never reaches here, because
    /// <see cref="IdempotentConsumer"/> only invokes its callback on
    /// <see cref="ConsumptionOutcome.Processed"/>), and the order-level
    /// fields (<c>status</c>, references, totals) are read back from
    /// <paramref name="applied"/> — the document AS IT STOOD immediately
    /// after the apply, never a value computed locally from the delta,
    /// because only the server knows whether this fact's rank actually
    /// raised the stored status.
    /// </summary>
    private static ReadModelDocument ToReadModelDocument(ProjectionDelta delta, BsonDocument applied)
    {
        // Both `references` and `totals` are ALWAYS embedded documents,
        // never an entirely-null field — PlaceholderDocument pre-creates
        // them with their own leaf fields null, precisely so the dotted
        // $set of an individual leaf never hits a path-conflict error.
        var references = applied[ReadModelCollection.Fields.References].AsBsonDocument;
        var totals = applied[ReadModelCollection.Fields.Totals].AsBsonDocument;

        return new ReadModelDocument(
            OrderId: delta.OrderId,
            OrderReference: NullableString(applied, ReadModelCollection.Fields.OrderReference),
            Status: applied[ReadModelCollection.Fields.Status].AsString,
            CancellationReason: NullableString(applied, ReadModelCollection.Fields.CancellationReason),
            DespatchReference: NullableString(references, ReadModelCollection.Fields.ReferencesFields.DespatchReference),
            InvoiceReference: NullableString(references, ReadModelCollection.Fields.ReferencesFields.InvoiceReference),
            PaymentReference: NullableString(references, ReadModelCollection.Fields.ReferencesFields.PaymentReference),
            InitialAmount: NullableLong(totals, ReadModelCollection.Fields.TotalsFields.InitialAmount),
            InitialDiscount: NullableLong(totals, ReadModelCollection.Fields.TotalsFields.InitialDiscount),
            TotalAmount: NullableLong(totals, ReadModelCollection.Fields.TotalsFields.TotalAmount),
            Currency: NullableString(applied, ReadModelCollection.Fields.Currency),
            EventId: delta.Entry.EventId,
            EventType: delta.Entry.EventType,
            OccurredAt: delta.Entry.OccurredAt,
            Summary: delta.Entry.Summary,
            CausationId: delta.Entry.CausationId);
    }

    private static string? NullableString(BsonDocument? document, string field)
    {
        if (document is null || !document.TryGetValue(field, out var value) || value.IsBsonNull)
        {
            return null;
        }

        return value.AsString;
    }

    private static long? NullableLong(BsonDocument? document, string field)
    {
        if (document is null || !document.TryGetValue(field, out var value) || value.IsBsonNull)
        {
            return null;
        }

        return value.ToInt64();
    }
}
