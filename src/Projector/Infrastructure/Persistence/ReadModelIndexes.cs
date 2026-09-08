using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <c>PR22</c>/<c>PR39</c> — creates the read model's indexes idempotently
/// at boot, before the first poll. <c>uq_order_reference</c> is built with
/// the IDENTICAL <c>Builders&lt;BsonDocument&gt;.Filter.Type(field, BsonType.String)</c>
/// call <c>MongoSeedWriter.EnsureIndexesAsync</c> uses (ledger <b>L12</b>) —
/// not because a hand-written <c>{ $type: "string" }</c> <c>BsonDocument</c>
/// has been observed to conflict with it (it does not: a real
/// <c>mongo:8.3.8</c> compares the string alias and the numeric BSON type
/// code as equivalent, in either creation order), but because the identical
/// call is the cheaper, self-documenting form, and because depending on
/// comparison-time equivalence beyond what is tested here is exactly the
/// kind of engine behaviour a future MongoDB version could change.
/// </summary>
public static class ReadModelIndexes
{
    public const string OrderReferenceIndexName = "uq_order_reference";
    public const string StatusUpdatedAtIndexName = "ix_status_updatedAt";

    public static async Task EnsureAsync(IMongoCollection<BsonDocument> collection, CancellationToken cancellationToken)
    {
        await CreateIndexAsync(
            collection,
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending(ReadModelCollection.Fields.OrderReference),
                new CreateIndexOptions<BsonDocument>
                {
                    Unique = true,
                    Name = OrderReferenceIndexName,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type(ReadModelCollection.Fields.OrderReference, BsonType.String),
                }),
            cancellationToken).ConfigureAwait(false);

        await CreateIndexAsync(
            collection,
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending(ReadModelCollection.Fields.Status)
                    .Descending(ReadModelCollection.Fields.UpdatedAt),
                new CreateIndexOptions { Name = StatusUpdatedAtIndexName }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>createIndex</c> is idempotent when the stored index matches, and
    /// raises a conflict error when it does not. THAT case fails the boot
    /// loudly, naming the index and the one-line fix, rather than silently
    /// running against a mismatched index (<c>PR22</c>, ledger <b>L11</b>).
    /// Two distinct server codes cover this, not one: <c>85</c>
    /// (<c>IndexOptionsConflict</c>) when only OPTIONS differ, and <c>86</c>
    /// (<c>IndexKeySpecsConflict</c>) when the index SPECIFICATION itself
    /// differs — which is what a real <c>mongo:8.3.8</c> raises for
    /// <c>PR22</c>'s own scenario (an existing plain unique index of the
    /// same name, versus this one's partial filter): a
    /// <c>partialFilterExpression</c> difference is apparently classified as
    /// a key-spec difference, not an options difference. Found live by
    /// <c>ReadModelIndexesTests</c>' own probe — design.md/#7 named only 85;
    /// #8's real server returns 86 for the scenario the requirement is
    /// actually about, which would have made this catch silently miss the
    /// one case <c>PR22</c> exists to guard, had it not been armed against a
    /// real container rather than assumed from the requirement text.
    /// </summary>
    private static async Task CreateIndexAsync(IMongoCollection<BsonDocument> collection, CreateIndexModel<BsonDocument> model, CancellationToken cancellationToken)
    {
        try
        {
            await collection.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict" || ex.Code is 85 or 86)
        {
            var indexName = model.Options.Name ?? "<unnamed>";
            throw new InvalidOperationException(
                $"Index '{indexName}' already exists on collection '{ReadModelCollection.Name}' with a different definition " +
                $"({ex.CodeName}, code {ex.Code}). Drop it and let the projector recreate it: " +
                $"db.{ReadModelCollection.Name}.dropIndex(\"{indexName}\")",
                ex);
        }
    }
}
