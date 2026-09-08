using MongoDB.Bson;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <c>PR10</c>'s causal timeline order, realised as ONE MongoDB aggregation
/// expression (design.md §5.4.3) — called by BOTH the live apply
/// (<see cref="DeltaToPipeline"/>) and the boot migration
/// (<see cref="TimelineOrderMigration"/>), so the two can never compute two
/// different orders (<c>PR32</c>). Depth is computed inside the expression
/// via a bounded <c>$reduce</c> fixpoint and DISCARDED with
/// <c>$unsetField</c> before the result is returned — it is never stored
/// (<c>PR31</c>).
/// </summary>
public static class TimelineOrder
{
    private const string DepthField = "__depth";

    /// <summary>
    /// <paramref name="entriesInput"/> is the aggregation expression that
    /// evaluates to the full array of entries to sort (e.g.
    /// <c>{ $concatArrays: [ existing, [newEntry] ] }</c> for the live
    /// apply, or plain <c>"$events"</c> for the migration).
    /// <paramref name="cap"/> bounds the fixpoint's depth so a fabricated
    /// cycle terminates deterministically rather than growing without
    /// bound.
    /// </summary>
    public static BsonValue Expression(BsonValue entriesInput, int cap)
    {
        var causeFilterCondition = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$$p.occurredAt", "$$e.occurredAt" }),
            new BsonDocument("$ne", new BsonArray { "$$p.eventId", "$$e.eventId" }),
            new BsonDocument("$eq", new BsonArray
            {
                "$$p.eventId",
                new BsonDocument("$ifNull", new BsonArray { "$$e.causationId", BsonNull.Value }),
            }),
        });

        var causeLookup = new BsonDocument("$arrayElemAt", new BsonArray
        {
            new BsonDocument("$filter", new BsonDocument
            {
                ["input"] = "$$prev",
                ["as"] = "p",
                ["cond"] = causeFilterCondition,
            }),
            0,
        });

        var depthExpression = new BsonDocument("$min", new BsonArray
        {
            cap,
            new BsonDocument("$add", new BsonArray
            {
                new BsonDocument("$ifNull", new BsonArray { "$$cause." + DepthField, -1 }),
                1,
            }),
        });

        var oneRelaxationRound = new BsonDocument("$let", new BsonDocument
        {
            ["vars"] = new BsonDocument("prev", "$$value"),
            ["in"] = new BsonDocument("$map", new BsonDocument
            {
                ["input"] = "$$prev",
                ["as"] = "e",
                ["in"] = new BsonDocument("$let", new BsonDocument
                {
                    ["vars"] = new BsonDocument("cause", causeLookup),
                    ["in"] = new BsonDocument("$mergeObjects", new BsonArray
                    {
                        "$$e",
                        new BsonDocument(DepthField, depthExpression),
                    }),
                }),
            }),
        });

        var initialValue = new BsonDocument("$map", new BsonDocument
        {
            ["input"] = "$$all",
            ["as"] = "e",
            ["in"] = new BsonDocument("$mergeObjects", new BsonArray
            {
                "$$e",
                new BsonDocument(DepthField, 0),
            }),
        });

        var ranked = new BsonDocument("$reduce", new BsonDocument
        {
            ["input"] = "$$all",
            ["initialValue"] = initialValue,
            ["in"] = oneRelaxationRound,
        });

        var sorted = new BsonDocument("$sortArray", new BsonDocument
        {
            ["input"] = "$$ranked",
            ["sortBy"] = new BsonDocument
            {
                ["occurredAt"] = 1,
                [DepthField] = 1,
                ["eventId"] = 1,
            },
        });

        var stripDepth = new BsonDocument("$map", new BsonDocument
        {
            ["input"] = sorted,
            ["as"] = "e",
            ["in"] = new BsonDocument("$unsetField", new BsonDocument
            {
                ["field"] = DepthField,
                ["input"] = "$$e",
            }),
        });

        return new BsonDocument("$let", new BsonDocument
        {
            ["vars"] = new BsonDocument("all", entriesInput),
            ["in"] = new BsonDocument("$let", new BsonDocument
            {
                ["vars"] = new BsonDocument("ranked", ranked),
                ["in"] = stripDepth,
            }),
        });
    }
}
