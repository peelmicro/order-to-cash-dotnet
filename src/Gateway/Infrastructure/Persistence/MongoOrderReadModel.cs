using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Projection;

namespace OrderToCash.Gateway.Infrastructure.Persistence;

/// <summary>
/// <see cref="IOrderReadModel"/> over a DIRECT, READ-ONLY MongoDB query
/// against the projector's OWN <c>order_timeline</c> collection (R54) — no
/// RPC hop: the projector answers no query subject by design
/// (<c>specs/projector_read_model/design.md</c> §2), and the contract
/// itself says list/detail are served "from the read model only". The
/// three internal-only fields the projector also stores
/// (<c>statusRank</c>, <c>timelineOrderVersion</c>, <c>processedEventKeys</c>)
/// are excluded at the query, so they can never reach the wire no matter
/// what a future field addition to the response DTOs would otherwise let
/// through. Ported from #7's <c>apps/gateway/src/infrastructure/persistence/mongo-order-read-model.adapter.ts</c>.
/// </summary>
public sealed class MongoOrderReadModel(IMongoCollection<BsonDocument> collection) : IOrderReadModel
{
    private static readonly ProjectionDefinition<BsonDocument> _excludeInternalFields = Builders<BsonDocument>.Projection
        .Exclude(GatewayReadModelCollection.Fields.StatusRank)
        .Exclude(GatewayReadModelCollection.Fields.TimelineOrderVersion)
        .Exclude(GatewayReadModelCollection.Fields.ProcessedEventKeys);

    public async Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(GatewayReadModelCollection.Fields.Id, orderId.ToString("D"));
        var doc = await collection.Find(filter).Project(_excludeInternalFields).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return doc is null ? null : ToDocument(doc);
    }

    public async Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(GatewayReadModelCollection.Fields.OrderReference, orderReference);
        var doc = await collection.Find(filter).Project(_excludeInternalFields).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return doc is null ? null : ToDocument(doc);
    }

    public async Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken)
    {
        var builder = Builders<BsonDocument>.Filter;
        var conditions = new List<FilterDefinition<BsonDocument>>();

        if (filter.Status is { Count: > 0 })
        {
            conditions.Add(builder.In(GatewayReadModelCollection.Fields.Status, filter.Status));
        }

        if (!string.IsNullOrEmpty(filter.RetailerCode))
        {
            conditions.Add(builder.Eq($"{GatewayReadModelCollection.Fields.Retailer}.{GatewayReadModelCollection.Fields.PartySnapshot.Code}", filter.RetailerCode));
        }

        if (!string.IsNullOrEmpty(filter.CompanyCode))
        {
            conditions.Add(builder.Eq($"{GatewayReadModelCollection.Fields.Company}.{GatewayReadModelCollection.Fields.PartySnapshot.Code}", filter.CompanyCode));
        }

        if (!string.IsNullOrEmpty(filter.OrderReference))
        {
            conditions.Add(builder.Eq(GatewayReadModelCollection.Fields.OrderReference, filter.OrderReference));
        }

        var query = conditions.Count == 0 ? builder.Empty : builder.And(conditions);
        var skip = (filter.Page - 1) * filter.PageSize;

        // openapi.yaml listOrders: "Ordering is by orderDate descending."
        var itemsTask = collection.Find(query)
            .Project(_excludeInternalFields)
            .Sort(Builders<BsonDocument>.Sort.Descending(GatewayReadModelCollection.Fields.OrderDate))
            .Skip(skip)
            .Limit(filter.PageSize)
            .ToListAsync(cancellationToken);
        var totalTask = collection.CountDocumentsAsync(query, cancellationToken: cancellationToken);

        await Task.WhenAll(itemsTask, totalTask).ConfigureAwait(false);

        return new OrderListResult(itemsTask.Result.Select(ToDocument).ToList(), totalTask.Result);
    }

    internal static OrderReadModelDocument ToDocument(BsonDocument doc)
    {
        var totals = doc[GatewayReadModelCollection.Fields.Totals].AsBsonDocument;
        var references = doc[GatewayReadModelCollection.Fields.References].AsBsonDocument;

        return new OrderReadModelDocument(
            Guid.Parse(doc[GatewayReadModelCollection.Fields.OrderId].AsString),
            NullableString(doc, GatewayReadModelCollection.Fields.OrderReference),
            NullableInstant(doc, GatewayReadModelCollection.Fields.OrderDate),
            ToParty(doc[GatewayReadModelCollection.Fields.Retailer].AsBsonDocument),
            ToParty(doc[GatewayReadModelCollection.Fields.Company].AsBsonDocument),
            doc[GatewayReadModelCollection.Fields.Status].AsString,
            NullableString(doc, GatewayReadModelCollection.Fields.CancellationReason),
            NullableString(doc, GatewayReadModelCollection.Fields.Currency),
            new OrderReadModelTotals(
                NullableLong(totals, GatewayReadModelCollection.Fields.TotalsFields.InitialAmount),
                NullableLong(totals, GatewayReadModelCollection.Fields.TotalsFields.InitialDiscount),
                NullableLong(totals, GatewayReadModelCollection.Fields.TotalsFields.TotalAmount)),
            doc[GatewayReadModelCollection.Fields.Items].AsBsonArray.Select(i => ToItem(i.AsBsonDocument)).ToList(),
            new OrderReadModelReferences(
                NullableString(references, GatewayReadModelCollection.Fields.ReferencesFields.DespatchReference),
                NullableString(references, GatewayReadModelCollection.Fields.ReferencesFields.InvoiceReference),
                NullableString(references, GatewayReadModelCollection.Fields.ReferencesFields.PaymentReference)),
            doc[GatewayReadModelCollection.Fields.Events].AsBsonArray.Select(e => ToEvent(e.AsBsonDocument)).ToList(),
            doc[GatewayReadModelCollection.Fields.HeaderComplete].AsBoolean,
            ParseInstant(doc[GatewayReadModelCollection.Fields.UpdatedAt].AsString));
    }

    private static OrderReadModelParty ToParty(BsonDocument party) => new(
        NullableString(party, GatewayReadModelCollection.Fields.PartySnapshot.Code),
        NullableString(party, GatewayReadModelCollection.Fields.PartySnapshot.Name),
        NullableString(party, GatewayReadModelCollection.Fields.PartySnapshot.Gln));

    private static OrderReadModelItem ToItem(BsonDocument item) => new(
        item[GatewayReadModelCollection.Fields.Item.ProductCode].AsString,
        NullableString(item, GatewayReadModelCollection.Fields.Item.Name),
        item[GatewayReadModelCollection.Fields.Item.Quantity].ToInt32(),
        item[GatewayReadModelCollection.Fields.Item.UnitPrice].ToInt64(),
        item[GatewayReadModelCollection.Fields.Item.LineDiscount].ToInt64());

    private static OrderReadModelEvent ToEvent(BsonDocument entry) => new(
        Guid.Parse(entry[GatewayReadModelCollection.Fields.Event.EventId].AsString),
        entry[GatewayReadModelCollection.Fields.Event.EventType].AsString,
        ParseInstant(entry[GatewayReadModelCollection.Fields.Event.OccurredAt].AsString),
        entry[GatewayReadModelCollection.Fields.Event.Summary].AsString,
        entry.TryGetValue(GatewayReadModelCollection.Fields.Event.Detail, out var detail) && detail.IsBsonDocument
            ? (IReadOnlyDictionary<string, object?>)detail.AsBsonDocument.Elements.ToDictionary(e => e.Name, e => BsonToObject(e.Value))
            : null,
        entry.TryGetValue(GatewayReadModelCollection.Fields.Event.CausationId, out var causationId) && causationId.IsString
            ? Guid.Parse(causationId.AsString)
            : null);

    private static object? BsonToObject(BsonValue value) => value.BsonType switch
    {
        BsonType.Null => null,
        BsonType.String => value.AsString,
        BsonType.Boolean => value.AsBoolean,
        BsonType.Int32 => value.AsInt32,
        BsonType.Int64 => value.AsInt64,
        BsonType.Double => value.AsDouble,
        BsonType.Document => value.AsBsonDocument.Elements.ToDictionary(e => e.Name, e => BsonToObject(e.Value)),
        BsonType.Array => value.AsBsonArray.Select(BsonToObject).ToList(),
        _ => value.ToString(),
    };

    private static string? NullableString(BsonDocument doc, string field)
    {
        if (!doc.TryGetValue(field, out var value) || value.IsBsonNull)
        {
            return null;
        }

        return value.AsString;
    }

    private static long? NullableLong(BsonDocument doc, string field)
    {
        if (!doc.TryGetValue(field, out var value) || value.IsBsonNull)
        {
            return null;
        }

        return value.ToInt64();
    }

    private static DateTimeOffset? NullableInstant(BsonDocument doc, string field)
    {
        var raw = NullableString(doc, field);
        return raw is null ? null : ParseInstant(raw);
    }

    private static DateTimeOffset ParseInstant(string wire) =>
        DateTimeOffset.Parse(wire, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
