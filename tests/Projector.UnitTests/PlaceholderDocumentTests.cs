using MongoDB.Bson;
using OrderToCash.Projector.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary><c>C2</c> — the placeholder is total over design.md §3.1's shape, in the seed's declaration order.</summary>
public sealed class PlaceholderDocumentTests
{
    private static readonly string[] _expectedTopLevelOrder =
    [
        "_id", "orderId", "orderReference", "orderDate", "retailer", "company", "status",
        "cancellationReason", "currency", "totals", "items", "references", "events",
        "headerComplete", "updatedAt", "statusRank", "timelineOrderVersion", "processedEventKeys",
    ];

    [Fact]
    public void WritesEveryKeyOfTheShapeInTheSeedsDeclarationOrder()
    {
        var doc = PlaceholderDocument.For(Guid.NewGuid(), "2026-03-04T10:00:00.000Z");
        Assert.Equal(_expectedTopLevelOrder, doc.Names);
    }

    [Fact]
    public void R53_IdAndOrderIdAreTheCorrelationIdAsALowercaseHyphenatedString()
    {
        var orderId = Guid.NewGuid();
        var doc = PlaceholderDocument.For(orderId, "2026-03-04T10:00:00.000Z");

        Assert.Equal(orderId.ToString("D"), doc["_id"].AsString);
        Assert.Equal(orderId.ToString("D"), doc["orderId"].AsString);
        Assert.False(doc["_id"].IsBsonBinaryData);
    }

    [Fact]
    public void R53_HeaderCompleteFalse_StatusPlaced_RankZero_VersionTwo_UpdatedAtTheTriggeringFactsOccurredAt()
    {
        var doc = PlaceholderDocument.For(Guid.NewGuid(), "2026-03-04T10:00:00.000Z");

        Assert.False(doc["headerComplete"].AsBoolean);
        Assert.Equal("placed", doc["status"].AsString);
        Assert.Equal(0, doc["statusRank"].AsInt32);
        Assert.Equal(2, doc["timelineOrderVersion"].AsInt32);
        Assert.Equal("2026-03-04T10:00:00.000Z", doc["updatedAt"].AsString);
    }

    [Fact]
    public void EveryUnknownScalarIsExplicitBsonNull_ArraysAreEmpty()
    {
        var doc = PlaceholderDocument.For(Guid.NewGuid(), "2026-03-04T10:00:00.000Z");

        Assert.True(doc["orderReference"].IsBsonNull);
        Assert.True(doc["orderDate"].IsBsonNull);
        Assert.True(doc["cancellationReason"].IsBsonNull);
        Assert.True(doc["currency"].IsBsonNull);

        Assert.True(doc["retailer"]["code"].IsBsonNull);
        Assert.True(doc["retailer"]["name"].IsBsonNull);
        Assert.True(doc["retailer"]["gln"].IsBsonNull);
        Assert.True(doc["company"]["code"].IsBsonNull);
        Assert.True(doc["totals"]["initialAmount"].IsBsonNull);
        Assert.True(doc["references"]["despatchReference"].IsBsonNull);

        Assert.Empty(doc["items"].AsBsonArray);
        Assert.Empty(doc["events"].AsBsonArray);
        Assert.Empty(doc["processedEventKeys"].AsBsonArray);
    }

    /// <summary><c>PR8</c>: two placeholders with a null orderReference must both be constructible (proves the shape does not itself collide — the partial index guard is proven live in ReadModelIndexesTests).</summary>
    [Fact]
    public void PR8_TwoPlaceholdersWithNullOrderReferenceCoexistUnderThePartialIndex()
    {
        var first = PlaceholderDocument.For(Guid.NewGuid(), "2026-03-04T10:00:00.000Z");
        var second = PlaceholderDocument.For(Guid.NewGuid(), "2026-03-04T10:00:00.000Z");

        Assert.True(first["orderReference"].IsBsonNull);
        Assert.True(second["orderReference"].IsBsonNull);
        Assert.NotEqual(first["_id"], second["_id"]);
    }
}
