using MongoDB.Bson;
using OrderToCash.Projector.Domain;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <c>ProjectionDelta</c> → the ONE <c>$set</c> aggregation-pipeline stage
/// that is the whole projection of one fact (design.md §5.3) — a PURE
/// function returning plain <c>BsonDocument</c>s, unit-testable with no
/// container. Every array and rank operand is <c>$ifNull</c>-guarded (ledger
/// <b>L8</b>): a missing <c>$statusRank</c> compares as BSON <c>null</c>,
/// which orders below every number, so an unguarded <c>$gt</c> could
/// regress a hand-inserted document with a status but no rank.
/// </summary>
public static class DeltaToPipeline
{
    private const int TimelineOrderVersion = 2;

    /// <summary>
    /// The number of entries the causal-order fixpoint needs to converge
    /// over — one MORE than the stored count, to also cover the entry being
    /// appended by this very apply.
    /// </summary>
    private const int MaxEntriesPerOrder = 32;

    public static BsonDocument[] For(ProjectionDelta delta, string dedupKey)
    {
        var newEntry = BuildEntryDocument(delta.Entry);

        var stage = new BsonDocument
        {
            [ReadModelCollection.Fields.ProcessedEventKeys] = new BsonDocument("$sortArray", new BsonDocument
            {
                ["input"] = new BsonDocument("$setUnion", new BsonArray
                {
                    new BsonDocument("$ifNull", new BsonArray { $"${ReadModelCollection.Fields.ProcessedEventKeys}", new BsonArray() }),
                    new BsonArray { dedupKey },
                }),
                ["sortBy"] = 1,
            }),

            [ReadModelCollection.Fields.Events] = TimelineOrder.Expression(
                new BsonDocument("$concatArrays", new BsonArray
                {
                    new BsonDocument("$ifNull", new BsonArray { $"${ReadModelCollection.Fields.Events}", new BsonArray() }),
                    new BsonArray { newEntry },
                }),
                MaxEntriesPerOrder),

            [ReadModelCollection.Fields.StatusRank] = new BsonDocument("$max", new BsonArray
            {
                new BsonDocument("$ifNull", new BsonArray { $"${ReadModelCollection.Fields.StatusRank}", 0 }),
                delta.StatusRank,
            }),

            [ReadModelCollection.Fields.Status] = delta.ImpliedStatus is null
                ? $"${ReadModelCollection.Fields.Status}"
                : new BsonDocument("$cond", new BsonDocument
                {
                    ["if"] = new BsonDocument("$gt", new BsonArray
                    {
                        delta.StatusRank,
                        new BsonDocument("$ifNull", new BsonArray { $"${ReadModelCollection.Fields.StatusRank}", 0 }),
                    }),
                    ["then"] = delta.ImpliedStatus,
                    ["else"] = $"${ReadModelCollection.Fields.Status}",
                }),

            [$"{ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.DespatchReference}"] =
                new BsonDocument("$ifNull", new BsonArray
                {
                    $"${ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.DespatchReference}",
                    ToBsonValue(delta.DespatchReferenceIfAbsent),
                }),
            [$"{ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.InvoiceReference}"] =
                new BsonDocument("$ifNull", new BsonArray
                {
                    $"${ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.InvoiceReference}",
                    ToBsonValue(delta.InvoiceReferenceIfAbsent),
                }),
            [$"{ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.PaymentReference}"] =
                new BsonDocument("$ifNull", new BsonArray
                {
                    $"${ReadModelCollection.Fields.References}.{ReadModelCollection.Fields.ReferencesFields.PaymentReference}",
                    ToBsonValue(delta.PaymentReferenceIfAbsent),
                }),

            [ReadModelCollection.Fields.CancellationReason] = new BsonDocument("$ifNull", new BsonArray
            {
                $"${ReadModelCollection.Fields.CancellationReason}",
                ToBsonValue(delta.CancellationReasonIfAbsent),
            }),

            [ReadModelCollection.Fields.UpdatedAt] = new BsonDocument("$max", new BsonArray
            {
                $"${ReadModelCollection.Fields.UpdatedAt}",
                InstantWire.Of(delta.Entry.OccurredAt),
            }),

            [ReadModelCollection.Fields.TimelineOrderVersion] = TimelineOrderVersion,
        };

        if (delta.Header is { } header)
        {
            stage[$"{ReadModelCollection.Fields.OrderReference}"] = header.OrderReference;
            stage[$"{ReadModelCollection.Fields.OrderDate}"] = InstantWire.Of(header.OrderDate);
            stage[$"{ReadModelCollection.Fields.Retailer}.{ReadModelCollection.Fields.PartySnapshot.Code}"] = header.RetailerCode;
            stage[$"{ReadModelCollection.Fields.Retailer}.{ReadModelCollection.Fields.PartySnapshot.Gln}"] = header.BuyerGln;
            stage[$"{ReadModelCollection.Fields.Company}.{ReadModelCollection.Fields.PartySnapshot.Code}"] = header.CompanyCode;
            stage[$"{ReadModelCollection.Fields.Company}.{ReadModelCollection.Fields.PartySnapshot.Gln}"] = header.SupplierGln;
            stage[ReadModelCollection.Fields.Currency] = header.Currency;
            stage[$"{ReadModelCollection.Fields.Totals}.{ReadModelCollection.Fields.TotalsFields.InitialAmount}"] = header.InitialAmount;
            stage[$"{ReadModelCollection.Fields.Totals}.{ReadModelCollection.Fields.TotalsFields.InitialDiscount}"] = header.InitialDiscount;
            stage[$"{ReadModelCollection.Fields.Totals}.{ReadModelCollection.Fields.TotalsFields.TotalAmount}"] = header.TotalAmount;
            stage[ReadModelCollection.Fields.Items] = new BsonArray(header.Items.Select(BuildItemDocument));
            stage[ReadModelCollection.Fields.HeaderComplete] = true;
        }

        // The pipeline is ONE stage — a $set — wrapping the whole field
        // map above. MongoDB requires every aggregation-pipeline stage
        // document to have EXACTLY ONE top-level field (its operator); the
        // fields above are the $set operator's own argument, never
        // top-level pipeline-stage fields themselves.
        return [new BsonDocument("$set", stage)];
    }

    private static BsonDocument BuildEntryDocument(TimelineEntryDelta entry)
    {
        var document = new BsonDocument
        {
            [ReadModelCollection.Fields.Event.EventId] = entry.EventId.ToString("D"),
            [ReadModelCollection.Fields.Event.EventType] = entry.EventType,
            [ReadModelCollection.Fields.Event.OccurredAt] = InstantWire.Of(entry.OccurredAt),
            [ReadModelCollection.Fields.Event.Summary] = entry.Summary,
        };

        if (entry.Detail is not null)
        {
            document[ReadModelCollection.Fields.Event.Detail] = DetailToBsonDocument(entry.Detail);
        }

        document[ReadModelCollection.Fields.Event.CausationId] = entry.CausationId.ToString("D");

        return document;
    }

    private static BsonDocument DetailToBsonDocument(IReadOnlyDictionary<string, object> detail)
    {
        // One conversion, camelCase keys, integers lossless by magnitude
        // (PR41, ledger L27) — through the SAME JsonWire.Options every wire
        // payload uses, so nested records serialise exactly as they do on
        // the fact stream, then parsed as BSON.
        var json = System.Text.Json.JsonSerializer.Serialize(detail, OrderToCash.Contracts.Wire.JsonWire.Options);
        return BsonDocument.Parse(json);
    }

    private static BsonDocument BuildItemDocument(OrderItemDelta item) => new()
    {
        [ReadModelCollection.Fields.Item.ProductCode] = item.ProductCode,
        [ReadModelCollection.Fields.Item.Name] = BsonNull.Value, // master data, on no fact (PR9).
        [ReadModelCollection.Fields.Item.Quantity] = item.Quantity,
        [ReadModelCollection.Fields.Item.UnitPrice] = item.UnitPrice,
        [ReadModelCollection.Fields.Item.LineDiscount] = item.LineDiscount,
    };

    private static BsonValue ToBsonValue(string? value) => value is null ? BsonNull.Value : value;
}
