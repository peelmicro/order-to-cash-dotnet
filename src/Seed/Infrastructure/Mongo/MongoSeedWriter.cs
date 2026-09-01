using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Seed.Domain.Data;
using OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.Infrastructure.Mongo;

/// <summary>
/// Writes the MongoDB <c>order_timeline</c> collection — one document per
/// seeded order, upserted by <c>_id</c> (the order id) so re-running the
/// seed is a plain idempotent <c>replaceOne(..., upsert: true)</c> per
/// document — ported from #7's <c>apps/seed/src/writers/mongo.writer.ts</c>.
/// </summary>
public static class MongoSeedWriter
{
    public const string CollectionName = "order_timeline";

    /// <summary>
    /// Same wire format as <see cref="OrderToCash.Contracts.Wire.InstantJsonConverter"/>
    /// (three fraction digits, literal <c>Z</c>) — the Databases doc §8
    /// documents every timeline date as a plain ISO string, matching #7's
    /// own <c>.toISOString()</c> calls, never a native BSON date.
    /// </summary>
    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static string Iso(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString(IsoFormat, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A local copy of the projector's own <c>TIMELINE_ORDER_VERSION</c>
    /// (#7's mongo.writer.ts Amendment A1) — a seeded document is written
    /// already at the current version, so the projector's boot migration
    /// never has reason to touch it.
    /// </summary>
    public const int TimelineOrderVersion = 2;

    private static readonly IReadOnlyDictionary<string, int> _statusRank = new Dictionary<string, int>
    {
        ["placed"] = 1,
        ["stock_reserved"] = 2,
        ["credit_approved"] = 3,
        ["confirmed"] = 4,
        ["despatched"] = 5,
        ["invoiced"] = 6,
        ["paid"] = 7,
        ["completed"] = 98,
        ["cancelled"] = 99,
    };

    public static IMongoCollection<OrderTimelineDocument> Collection(IMongoDatabase database) =>
        database.GetCollection<OrderTimelineDocument>(CollectionName);

    /// <summary>
    /// PARTIAL, not plain unique (projector_read_model open point 1): the
    /// projector's own placeholder documents carry <c>orderReference: null</c>,
    /// and a plain unique index would reject the second placeholder with
    /// E11000. Restricting the index to documents where <c>orderReference</c>
    /// is a string leaves every seeded document (which always has one)
    /// covered, while placeholders sit outside the index entirely.
    /// </summary>
    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var collection = Collection(database);
        var keys = Builders<OrderTimelineDocument>.IndexKeys.Ascending(d => d.OrderReference);
        var options = new CreateIndexOptions<OrderTimelineDocument>
        {
            Unique = true,
            Name = "uq_order_reference",
            PartialFilterExpression = Builders<OrderTimelineDocument>.Filter.Type(d => d.OrderReference, BsonType.String),
        };

        await collection.Indexes
            .CreateOneAsync(new CreateIndexModel<OrderTimelineDocument>(keys, options), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static OrderTimelineDocument ToTimelineDocument(OrderSagaFixture saga)
    {
        var retailer = Retailers.ByCode(saga.RetailerCode);
        var company = Companies.ByCode(saga.CompanyCode);

        return new OrderTimelineDocument
        {
            Id = saga.OrderId.ToString(),
            OrderId = saga.OrderId.ToString(),
            OrderReference = saga.OrderReference,
            OrderDate = Iso(saga.OrderDate),
            Retailer = new PartySnapshot { Code = retailer.Code, Name = retailer.Name, Gln = retailer.Gln },
            Company = new PartySnapshot { Code = company.Code, Name = company.Name, Gln = company.Gln },
            Status = saga.Status,
            CancellationReason = saga.CancellationReason,
            Currency = saga.Currency,
            Totals = new Totals
            {
                InitialAmount = saga.InitialAmount,
                InitialDiscount = saga.InitialDiscount,
                TotalAmount = saga.TotalAmount,
            },
            Items =
            [
                .. saga.Lines.Select(line => new TimelineItem
                {
                    ProductCode = line.ProductCode,
                    Name = Products.ByCode(line.ProductCode).Name,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineDiscount = line.LineDiscount,
                }),
            ],
            References = new References
            {
                DespatchReference = saga.Despatch?.DespatchReference,
                InvoiceReference = saga.Invoice?.InvoiceReference,
                PaymentReference = saga.Invoice?.Payment.PaymentReference,
            },
            // Ordered by occurredAt — SagaFixtures already constructs the
            // timeline in causal/occurredAt order; sorted again here
            // defensively so the read model never trusts construction order alone.
            Events =
            [
                .. saga.Timeline
                    .OrderBy(entry => entry.OccurredAt)
                    .Select(entry => new TimelineEvent
                    {
                        EventId = entry.EventId.ToString(),
                        EventType = entry.EventType,
                        OccurredAt = Iso(entry.OccurredAt),
                        Summary = entry.Summary,
                        Detail = entry.Detail is null ? null : new Dictionary<string, object>(entry.Detail),
                        CausationId = entry.CausationId.ToString(),
                    }),
            ],
            HeaderComplete = true,
            UpdatedAt = Iso(saga.UpdatedAt),
            StatusRank = _statusRank.TryGetValue(saga.Status, out var rank)
                ? rank
                : throw new InvalidOperationException($"mongo writer: unknown order status \"{saga.Status}\" has no rank"),
            TimelineOrderVersion = TimelineOrderVersion,
            ProcessedEventKeys =
            [
                .. saga.Timeline
                    .Select(entry => $"projector:{entry.EventId}")
                    .OrderBy(key => key, StringComparer.Ordinal),
            ],
        };
    }

    public static async Task SeedTimelinesAsync(
        IMongoDatabase database,
        IReadOnlyList<OrderSagaFixture>? sagas = null,
        CancellationToken cancellationToken = default)
    {
        sagas ??= SagaFixtures.All;

        await EnsureIndexesAsync(database, cancellationToken).ConfigureAwait(false);

        var collection = Collection(database);
        foreach (var saga in sagas)
        {
            var document = ToTimelineDocument(saga);
            await collection
                .ReplaceOneAsync(
                    Builders<OrderTimelineDocument>.Filter.Eq(d => d.Id, document.Id),
                    document,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static Task<long> CountTimelinesAsync(IMongoDatabase database, CancellationToken cancellationToken = default) =>
        Collection(database).CountDocumentsAsync(FilterDefinition<OrderTimelineDocument>.Empty, cancellationToken: cancellationToken);
}
