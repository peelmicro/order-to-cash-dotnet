using MongoDB.Bson;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// <c>PR8</c>'s skeleton — TOTAL over design.md §3.1's shape, in the seed
/// class's own declaration order, so a later apply never has to append a key
/// the placeholder forgot (which would silently land it at the end of the
/// document and break <c>PR15</c>/<c>PR44</c>'s element-order comparison —
/// ledger <b>L17</b>). Every unknown scalar is an explicit
/// <see cref="BsonNull.Value"/>; every embedded object the live apply later
/// writes dotted paths into (<c>retailer</c>, <c>company</c>, <c>totals</c>,
/// <c>references</c>) is pre-created with its own leaf fields null, because
/// a dotted <c>$set</c> onto a field whose current value IS <c>null</c>
/// (rather than an object) raises a path-conflict error.
/// </summary>
/// <remarks>
/// <b>Why Phase 1 is a pipeline <c>$replaceWith</c>, not the classic
/// <c>$setOnInsert</c> update operator design.md §5.1 illustrates.</b>
/// Probed directly against a real <c>mongo:8.3.8</c>: the classic
/// <c>{ $setOnInsert: &lt;wholeDocument&gt; }</c> operator does NOT preserve
/// the document's field order on insert — the server applies the modifier's
/// fields through its update-path machinery and the resulting document
/// comes out with its TOP-LEVEL keys re-ordered ALPHABETICALLY, silently
/// breaking <c>PR15</c>/<c>PR44</c>'s element-order comparison regardless of
/// how total this class is over the shape. An aggregation-PIPELINE update
/// (<c>{ $replaceWith: &lt;expr&gt; }</c>) does not go through that
/// machinery — a literal document embedded in the pipeline is written
/// byte-for-byte in the order it was written in code. This is therefore a
/// SECOND, independent instance of ledger row L17 (the first being the
/// placeholder's own totality), found live in this feature's own
/// implementation and not by #7, whose JavaScript object literal and
/// `{ upsert: true }` on a classic <c>updateOne</c> never exhibited it —
/// #7's own MongoDB node driver / server combination evidently preserves
/// classic-operator insert order (or #7 never wrote a test strict enough to
/// notice); #8's proof is the direct probe above and
/// <c>PlaceholderDocumentTests</c>' own live assertion.
/// </remarks>
public static class PlaceholderDocument
{
    public static BsonDocument For(Guid orderId, string occurredAtWire)
    {
        var id = orderId.ToString("D");

        return new BsonDocument
        {
            [ReadModelCollection.Fields.Id] = id,
            [ReadModelCollection.Fields.OrderId] = id,
            [ReadModelCollection.Fields.OrderReference] = BsonNull.Value,
            [ReadModelCollection.Fields.OrderDate] = BsonNull.Value,
            [ReadModelCollection.Fields.Retailer] = new BsonDocument
            {
                [ReadModelCollection.Fields.PartySnapshot.Code] = BsonNull.Value,
                [ReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
                [ReadModelCollection.Fields.PartySnapshot.Gln] = BsonNull.Value,
            },
            [ReadModelCollection.Fields.Company] = new BsonDocument
            {
                [ReadModelCollection.Fields.PartySnapshot.Code] = BsonNull.Value,
                [ReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
                [ReadModelCollection.Fields.PartySnapshot.Gln] = BsonNull.Value,
            },
            [ReadModelCollection.Fields.Status] = "placed",
            [ReadModelCollection.Fields.CancellationReason] = BsonNull.Value,
            [ReadModelCollection.Fields.Currency] = BsonNull.Value,
            [ReadModelCollection.Fields.Totals] = new BsonDocument
            {
                [ReadModelCollection.Fields.TotalsFields.InitialAmount] = BsonNull.Value,
                [ReadModelCollection.Fields.TotalsFields.InitialDiscount] = BsonNull.Value,
                [ReadModelCollection.Fields.TotalsFields.TotalAmount] = BsonNull.Value,
            },
            [ReadModelCollection.Fields.Items] = new BsonArray(),
            [ReadModelCollection.Fields.References] = new BsonDocument
            {
                [ReadModelCollection.Fields.ReferencesFields.DespatchReference] = BsonNull.Value,
                [ReadModelCollection.Fields.ReferencesFields.InvoiceReference] = BsonNull.Value,
                [ReadModelCollection.Fields.ReferencesFields.PaymentReference] = BsonNull.Value,
            },
            [ReadModelCollection.Fields.Events] = new BsonArray(),
            [ReadModelCollection.Fields.HeaderComplete] = false,
            [ReadModelCollection.Fields.UpdatedAt] = occurredAtWire,
            [ReadModelCollection.Fields.StatusRank] = 0,
            [ReadModelCollection.Fields.TimelineOrderVersion] = 2,
            [ReadModelCollection.Fields.ProcessedEventKeys] = new BsonArray(),
        };
    }

    /// <summary>
    /// The Phase-1 pipeline (design.md §5.1): on an upsert-INSERT, replaces
    /// the document with the placeholder verbatim, in order; on a MATCH
    /// (the document already exists), replaces it with itself — a true
    /// no-op, never touching <c>events</c>/<c>status</c>/<c>references</c>/
    /// <c>processedEventKeys</c> of a pre-existing document.
    /// <c>orderId</c> is the field tested for "missing", never <c>_id</c>:
    /// MongoDB seeds <c>$$ROOT</c> with <c>{ _id: &lt;filter's _id&gt; }</c>
    /// (not <c>{}</c>) when a pipeline-based upsert has no match, so testing
    /// <c>_id</c> for "missing" is always false and would make this a
    /// permanent no-op that never inserts anything — found live by the same
    /// direct probe as the class remarks.
    /// </summary>
    public static BsonDocument[] UpsertPipeline(Guid orderId, string occurredAtWire)
    {
        var placeholder = For(orderId, occurredAtWire);

        var isNewDocument = new BsonDocument("$eq", new BsonArray
        {
            new BsonDocument("$type", $"${ReadModelCollection.Fields.OrderId}"),
            "missing",
        });

        var replaceWith = new BsonDocument("$cond", new BsonArray { isNewDocument, placeholder, "$$ROOT" });

        return [new BsonDocument("$replaceWith", replaceWith)];
    }
}
