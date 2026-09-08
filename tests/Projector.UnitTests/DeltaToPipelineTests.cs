using MongoDB.Bson;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

public sealed class DeltaToPipelineTests
{
    private static TimelineEntryDelta Entry(Guid? eventId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null, IReadOnlyDictionary<string, object>? detail = null) =>
        new(
            eventId ?? Guid.NewGuid(),
            "order.confirmed.v1",
            occurredAt ?? DateTimeOffset.UtcNow,
            "Order confirmed (ORDRSP)",
            detail,
            causationId ?? Guid.NewGuid());

    /// <summary>The pipeline's one <c>$set</c> stage's argument document — every other test in this file inspects this, never the raw pipeline array.</summary>
    private static BsonDocument SetStage(ProjectionDelta delta, string dedupKey)
    {
        var stages = DeltaToPipeline.For(delta, dedupKey);
        Assert.Single(stages);
        var stage = stages[0];
        Assert.Single(stage.Names); // exactly one top-level operator — $set.
        Assert.Equal("$set", stage.GetElement(0).Name);
        return stage["$set"].AsBsonDocument;
    }

    /// <summary><c>C5</c> — the stage array has length 1, whatever the delta shape. Arm by splitting into two stages.</summary>
    [Fact]
    public void PR6_TheEmittedPipelineIsExactlyOneStage()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "confirmed", 4, null, null, null, null, null);
        var stages = DeltaToPipeline.For(delta, "projector:" + Guid.NewGuid());
        Assert.Single(stages);
    }

    [Fact]
    public void PR12_AStatusImplyingFact_SetsStatusRankViaMaxAndStatusViaCond()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "confirmed", 4, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        Assert.True(stage["statusRank"].IsBsonDocument);
        Assert.Equal("$max", stage["statusRank"].AsBsonDocument.GetElement(0).Name);
        Assert.True(stage["status"].IsBsonDocument);
        Assert.Equal("$cond", stage["status"].AsBsonDocument.GetElement(0).Name);
    }

    [Fact]
    public void PR12_AStatusLessFact_RankZero_LeavesStatusAsASelfAssignment()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), null, 0, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        // status-less: no $cond — status is rewritten to its own current
        // value ($status), a total no-op rather than a conditional branch.
        Assert.Equal("$status", stage["status"].AsString);

        Assert.True(stage["statusRank"].IsBsonDocument);
        var maxOperands = stage["statusRank"]["$max"].AsBsonArray;
        Assert.Equal(0, maxOperands[1].AsInt32);
    }

    [Fact]
    public void PR9_OrderPlacedHeaderFieldsArePresentAndHeaderCompleteTrue_WithNoIfNull()
    {
        var header = new OrderHeaderDelta(
            "ORD-000001", DateTimeOffset.UtcNow, "RET01", "buyer-gln", "COM01", "supplier-gln", "EUR",
            1000, 0, 1000, [new OrderItemDelta("SKU1", 2, 500, 0)]);
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "placed", 1, null, null, null, null, header);

        var stage = SetStage(delta, "projector:x");

        Assert.Equal("ORD-000001", stage["orderReference"].AsString);
        Assert.True(stage["headerComplete"].AsBoolean);
        Assert.Equal("RET01", stage["retailer.code"].AsString); // no $ifNull — unconditional write
        Assert.Equal("COM01", stage["company.code"].AsString);
        Assert.Equal(1000L, stage["totals.initialAmount"].AsInt64);
        Assert.IsType<BsonArray>(stage["items"]);
    }

    /// <summary><c>PR11</c> — $ifNull on THAT reference only.</summary>
    [Fact]
    public void PR11_AReferenceCarryingFactAppliesIfNullToItsOwnReferenceOnly()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "despatched", 5, null, "DES-000001", null, null, null);
        var stage = SetStage(delta, "projector:x");

        Assert.True(stage["references.despatchReference"].IsBsonDocument);
        Assert.Equal("$ifNull", stage["references.despatchReference"].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("DES-000001", stage["references.despatchReference"]["$ifNull"].AsBsonArray[1].AsString);

        // The other two references are STILL $ifNull-guarded, but with a
        // null fill value — never overwritten with something invented.
        Assert.True(stage["references.invoiceReference"]["$ifNull"].AsBsonArray[1].IsBsonNull);
        Assert.True(stage["references.paymentReference"]["$ifNull"].AsBsonArray[1].IsBsonNull);
    }

    /// <summary><c>PR13</c> — $ifNull only on cancellationReason.</summary>
    [Fact]
    public void PR13_OrderCancelledAppliesIfNullOnlyToCancellationReason()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "cancelled", 99, "buyer_requested", null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        Assert.True(stage["cancellationReason"].IsBsonDocument);
        Assert.Equal("buyer_requested", stage["cancellationReason"]["$ifNull"].AsBsonArray[1].AsString);
        Assert.True(stage["references.despatchReference"]["$ifNull"].AsBsonArray[1].IsBsonNull);
    }

    /// <summary><c>C5</c>: every array/rank operand is $ifNull-guarded. Arm by deleting the $ifNull from one reference operand.</summary>
    [Fact]
    public void EveryArrayAndRankOperandIsIfNullGuarded()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "confirmed", 4, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        Assert.Equal("$ifNull", stage["statusRank"]["$max"].AsBsonArray[0].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("$ifNull", stage["processedEventKeys"]["$sortArray"]["input"]["$setUnion"].AsBsonArray[0].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("$ifNull", stage["references.despatchReference"].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("$ifNull", stage["references.invoiceReference"].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("$ifNull", stage["references.paymentReference"].AsBsonDocument.GetElement(0).Name);
        Assert.Equal("$ifNull", stage["cancellationReason"].AsBsonDocument.GetElement(0).Name);
    }

    /// <summary><c>PR10</c>/<c>C6</c>: sortBy is exactly occurredAt/__depth/eventId in that order, and __depth never survives in the output. Arm by reordering the sort keys or removing $unsetField.</summary>
    [Fact]
    public void PR10_TheEventsStageIsTheCausalTimelineOrderExpression_WithDepthComputedAndDiscarded()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "confirmed", 4, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        var eventsExpr = stage["events"].AsBsonDocument; // { $let: ... }
        var text = eventsExpr.ToJson();

        Assert.Contains("\"$sortArray\"", text, StringComparison.Ordinal);
        Assert.Contains("\"$unsetField\"", text, StringComparison.Ordinal);

        // Walk down to the sortBy document to check key ORDER specifically.
        var inner = eventsExpr["$let"]["in"].AsBsonDocument["$let"]["in"].AsBsonDocument; // outer $let -> in -> inner $let -> in ($map over $sortArray)
        var sortArray = inner["$map"]["input"].AsBsonDocument["$sortArray"].AsBsonDocument;
        var sortBy = sortArray["sortBy"].AsBsonDocument;
        var keys = sortBy.Names.ToList();
        Assert.Equal(["occurredAt", "__depth", "eventId"], keys);
        Assert.Equal(1, sortBy["occurredAt"].AsInt32);
        Assert.Equal(1, sortBy["__depth"].AsInt32);
        Assert.Equal(1, sortBy["eventId"].AsInt32);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void PR41_ElementNamesAreTheSeedDocumentsBsonElementNames_IdsAreLowercaseHyphenatedStrings_InstantsAreTheWireFormat(string eventIdSeed)
    {
        var eventId = Guid.Parse(eventIdSeed);
        var causationId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 3, 4, 10, 15, 0, 0, TimeSpan.Zero);
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(eventId, causationId, occurredAt), null, 0, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");

        var eventsInputArray = stage["events"]["$let"]["vars"]["all"].AsBsonDocument["$concatArrays"].AsBsonArray;
        var newEntryDoc = eventsInputArray[1].AsBsonArray[0].AsBsonDocument;

        Assert.Equal(eventId.ToString("D"), newEntryDoc["eventId"].AsString);
        Assert.False(newEntryDoc["eventId"].IsBsonBinaryData);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", newEntryDoc["occurredAt"].AsString);
        Assert.Equal("2026-03-04T10:15:00.000Z", newEntryDoc["occurredAt"].AsString);
        Assert.Equal(causationId.ToString("D"), newEntryDoc["causationId"].AsString);
    }

    [Fact]
    public void PR41_MoneyIsInt64_QuantityIsInt32_AndADetailAmountAboveInt32MaxSurvivesAsInt64()
    {
        var header = new OrderHeaderDelta(
            "ORD-1", DateTimeOffset.UtcNow, "RET1", "b", "COM1", "s", "EUR",
            1000, 0, 1000, [new OrderItemDelta("SKU1", 3, 500, 10)]);
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), "placed", 1, null, null, null, null, header);
        var stage = SetStage(delta, "projector:x");

        Assert.Equal(BsonType.Int64, stage["totals.initialAmount"].BsonType);
        Assert.Equal(BsonType.Int64, stage["totals.initialDiscount"].BsonType);
        Assert.Equal(BsonType.Int64, stage["totals.totalAmount"].BsonType);

        var item = ((BsonArray)stage["items"])[0].AsBsonDocument;
        Assert.Equal(BsonType.Int32, item["quantity"].BsonType);
        Assert.Equal(BsonType.Int64, item["unitPrice"].BsonType);
        Assert.Equal(BsonType.Int64, item["lineDiscount"].BsonType);

        // A detail integer above int.MaxValue survives as Int64, same value.
        long aboveIntMax = (long)int.MaxValue + 100;
        var detail = new Dictionary<string, object> { ["amount"] = aboveIntMax };
        var withDetail = new ProjectionDelta(Guid.NewGuid(), Entry(detail: detail), null, 0, null, null, null, null, null);
        var stageWithDetail = SetStage(withDetail, "projector:y");
        var newEntryDoc = stageWithDetail["events"]["$let"]["vars"]["all"].AsBsonDocument["$concatArrays"].AsBsonArray[1].AsBsonArray[0].AsBsonDocument;
        Assert.Equal(BsonType.Int64, newEntryDoc["detail"]["amount"].BsonType);
        Assert.Equal(aboveIntMax, newEntryDoc["detail"]["amount"].AsInt64);
    }

    [Fact]
    public void TimelineOrderVersionIsStampedOnEveryWrite()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(), null, 0, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");
        Assert.Equal(2, stage["timelineOrderVersion"].AsInt32);
    }

    [Fact]
    public void DetailAbsentWhenTheFactHasNone()
    {
        var delta = new ProjectionDelta(Guid.NewGuid(), Entry(detail: null), null, 0, null, null, null, null, null);
        var stage = SetStage(delta, "projector:x");
        var newEntryDoc = stage["events"]["$let"]["vars"]["all"].AsBsonDocument["$concatArrays"].AsBsonArray[1].AsBsonArray[0].AsBsonDocument;
        Assert.False(newEntryDoc.Contains("detail"));
    }
}
