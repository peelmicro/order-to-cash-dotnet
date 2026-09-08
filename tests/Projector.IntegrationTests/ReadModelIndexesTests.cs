using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR22</c>, <c>PR39</c> — design.md §10.6 flagged the partial filter's
/// stored form (ledger <b>L12</b>) as the row "most likely to bite", and it
/// did not: a real <c>mongo:8.3.8</c> compares a hand-built
/// <c>{ $type: "string" }</c> filter and the seed's
/// <c>Builders&lt;...&gt;.Filter.Type(field, BsonType.String)</c>-rendered
/// one as the SAME partial filter, in either creation order, so neither
/// raises a conflict against the other — see
/// <c>L12_TheServerComparesTheTypeAliasesAsEquivalentButStoresWhicheverRenderingCreatedTheIndex_InBothCreationOrders</c>
/// below. The production code still builds <c>uq_order_reference</c> with
/// the IDENTICAL call the seed uses, not because a conflict was observed,
/// but because it is the cheaper, self-documenting form.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class ReadModelIndexesTests(MongoContainerFixture mongoFixture)
{
    [Fact]
    public async Task PR22_CreatesThePartialOrderReferenceIndexAndTheStatusUpdatedAtIndex_IdempotentlyOnASecondRun()
    {
        var collection = mongoFixture.FreshCollection("indexes-idempotent");

        await ReadModelIndexes.EnsureAsync(collection, CancellationToken.None);
        await ReadModelIndexes.EnsureAsync(collection, CancellationToken.None); // second run — must not throw.

        var indexNames = await (await collection.Indexes.ListAsync()).ToListAsync();
        var names = indexNames.Select(i => i["name"].AsString).ToList();
        Assert.Contains(ReadModelIndexes.OrderReferenceIndexName, names);
        Assert.Contains(ReadModelIndexes.StatusUpdatedAtIndexName, names);

        // The partial nature: TWO documents with orderReference: null must coexist.
        await collection.InsertOneAsync(PlaceholderDocument.For(Guid.NewGuid(), "2026-01-01T00:00:00.000Z"));
        await collection.InsertOneAsync(PlaceholderDocument.For(Guid.NewGuid(), "2026-01-01T00:00:00.000Z"));
        var count = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PR22_RefusesToStartAgainstANonPartialIndexOfTheSameName_NamingTheIndex()
    {
        var collection = mongoFixture.FreshCollection("indexes-conflict");

        // Create a NON-partial unique index of the SAME name first.
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(ReadModelCollection.Fields.OrderReference),
            new CreateIndexOptions { Unique = true, Name = ReadModelIndexes.OrderReferenceIndexName }));

        // A second placeholder insert would fail under the wrong (non-partial) index.
        await collection.InsertOneAsync(PlaceholderDocument.For(Guid.NewGuid(), "2026-01-01T00:00:00.000Z"));
        await Assert.ThrowsAsync<MongoWriteException>(() => collection.InsertOneAsync(PlaceholderDocument.For(Guid.NewGuid(), "2026-01-01T00:00:00.000Z")));

        // Now the projector must refuse to start against it, naming the index.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadModelIndexes.EnsureAsync(collection, CancellationToken.None));
        Assert.Contains(ReadModelIndexes.OrderReferenceIndexName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("dropIndex", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The row design.md §10.6 names first: running the seed's index creation then the projector's, and the reverse, on ONE collection, raises no IndexOptionsConflict.</summary>
    [Fact]
    public async Task PR39_TheSeedsIndexThenTheProjectors_AndTheProjectorsThenTheSeeds_RaiseNoIndexOptionsConflict()
    {
        var seedFirstDatabase = mongoFixture.FreshDatabase("idx-seed-first");
        var seedFirstCollection = seedFirstDatabase.GetCollection<BsonDocument>(ReadModelCollection.Name);
        await MongoSeedWriter.EnsureIndexesAsync(seedFirstDatabase, CancellationToken.None);
        var seedFirstException = await Record.ExceptionAsync(() => ReadModelIndexes.EnsureAsync(seedFirstCollection, CancellationToken.None));
        Assert.Null(seedFirstException);

        var projectorFirstDatabase = mongoFixture.FreshDatabase("idx-projector-first");
        var projectorFirstCollection = projectorFirstDatabase.GetCollection<BsonDocument>(ReadModelCollection.Name);
        await ReadModelIndexes.EnsureAsync(projectorFirstCollection, CancellationToken.None);
        var projectorFirstException = await Record.ExceptionAsync(() => MongoSeedWriter.EnsureIndexesAsync(projectorFirstDatabase, CancellationToken.None));
        Assert.Null(projectorFirstException);
    }

    /// <summary>
    /// The answer to design §10's L12, corrected in the SECOND fix round
    /// after review defect D4 (the first fix round's own D1 correction gave
    /// the right operational answer with the wrong mechanism). On a real
    /// <c>mongo:8.3.8</c> server the <c>$type</c> alias <c>"string"</c> and
    /// the numeric BSON type code <c>2</c> are treated as EQUIVALENT when the
    /// server COMPARES an existing index's specification against a requested
    /// one — so a hand-built <c>BsonDocument</c> filter using the string
    /// alias and the seed's <c>Builders&lt;&gt;.Filter.Type</c>-rendered one
    /// never raise <c>IndexOptionsConflict</c>, in EITHER creation order.
    /// The server does NOT normalise the alias when STORING the index:
    /// <c>getIndexes()</c> returns whichever rendering actually created it.
    /// Both directions are exercised here, and both were independently
    /// verified directly against the server with <c>mongosh</c>:
    ///
    /// A) numeric created first (<see cref="MongoSeedWriter.EnsureIndexesAsync"/>),
    ///    then the hand-built string-alias filter against it — ACCEPTED, no
    ///    conflict, stored <c>$type</c> reads back as the NUMERIC code
    ///    <c>2</c> (the rendering that created it).
    /// B) the hand-built string-alias filter created first, then
    ///    <see cref="MongoSeedWriter.EnsureIndexesAsync"/>'s numeric
    ///    rendering against it — ACCEPTED, no conflict, stored
    ///    <c>$type</c> reads back as the STRING alias <c>"string"</c> (the
    ///    rendering that created it).
    ///
    /// The production code keeps building the filter via the identical
    /// <c>Builders&lt;&gt;.Filter.Type</c> call the seed uses — not because a
    /// conflict was observed in either order, but because it is the cheaper,
    /// more self-documenting form, and because depending on comparison-time
    /// equivalence beyond what this test proves is exactly the kind of
    /// engine behaviour a future MongoDB version could change.
    /// </summary>
    [Fact]
    public async Task L12_TheServerComparesTheTypeAliasesAsEquivalentButStoresWhicheverRenderingCreatedTheIndex_InBothCreationOrders()
    {
        var handBuiltOptions = new CreateIndexOptions<BsonDocument>
        {
            Unique = true,
            Name = ReadModelIndexes.OrderReferenceIndexName,
            PartialFilterExpression = new BsonDocument(ReadModelCollection.Fields.OrderReference, new BsonDocument("$type", "string")),
        };
        var handBuiltModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending(ReadModelCollection.Fields.OrderReference), handBuiltOptions);

        // A) numeric first (the seed's rendering), then the hand-built string alias against it.
        var numericFirstDatabase = mongoFixture.FreshDatabase("idx-l12-numeric-first");
        await MongoSeedWriter.EnsureIndexesAsync(numericFirstDatabase, CancellationToken.None);
        var numericFirstCollection = numericFirstDatabase.GetCollection<BsonDocument>(ReadModelCollection.Name);

        var numericFirstException = await Record.ExceptionAsync(() => numericFirstCollection.Indexes.CreateOneAsync(handBuiltModel));
        Assert.Null(numericFirstException);

        var numericFirstIndexes = await (await numericFirstCollection.Indexes.ListAsync()).ToListAsync();
        var numericFirstStoredIndex = numericFirstIndexes.Single(i => i["name"].AsString == ReadModelIndexes.OrderReferenceIndexName);
        Assert.Equal(2, numericFirstStoredIndex["partialFilterExpression"][ReadModelCollection.Fields.OrderReference]["$type"].AsInt32);

        // B) the hand-built string alias first, then the seed's numeric rendering against it.
        var stringFirstDatabase = mongoFixture.FreshDatabase("idx-l12-string-first");
        var stringFirstCollection = stringFirstDatabase.GetCollection<BsonDocument>(ReadModelCollection.Name);
        await stringFirstCollection.Indexes.CreateOneAsync(handBuiltModel);

        var stringFirstException = await Record.ExceptionAsync(() => MongoSeedWriter.EnsureIndexesAsync(stringFirstDatabase, CancellationToken.None));
        Assert.Null(stringFirstException);

        var stringFirstIndexes = await (await stringFirstCollection.Indexes.ListAsync()).ToListAsync();
        var stringFirstStoredIndex = stringFirstIndexes.Single(i => i["name"].AsString == ReadModelIndexes.OrderReferenceIndexName);
        Assert.Equal("string", stringFirstStoredIndex["partialFilterExpression"][ReadModelCollection.Fields.OrderReference]["$type"].AsString);
    }
}
