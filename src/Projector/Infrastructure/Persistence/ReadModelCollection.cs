using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// The <c>otc_read_model.order_timeline</c> collection name, and every
/// camelCase element-name constant of design.md §3.1/§3.2 — the same
/// literals <c>OrderTimelineDocument</c>'s <c>[BsonElement]</c> attributes
/// carry (<c>PR41</c>), collected here rather than inferred from a
/// re-declared class map on the write path (design.md §3, §5.5's rejection
/// table).
/// </summary>
public static class ReadModelCollection
{
    /// <summary>Equal to <c>MongoSeedWriter.CollectionName</c>.</summary>
    public const string Name = "order_timeline";

    public static class Fields
    {
        public const string Id = "_id";
        public const string OrderId = "orderId";
        public const string OrderReference = "orderReference";
        public const string OrderDate = "orderDate";
        public const string Retailer = "retailer";
        public const string Company = "company";
        public const string Status = "status";
        public const string CancellationReason = "cancellationReason";
        public const string Currency = "currency";
        public const string Totals = "totals";
        public const string Items = "items";
        public const string References = "references";
        public const string Events = "events";
        public const string HeaderComplete = "headerComplete";
        public const string UpdatedAt = "updatedAt";
        public const string StatusRank = "statusRank";
        public const string TimelineOrderVersion = "timelineOrderVersion";
        public const string ProcessedEventKeys = "processedEventKeys";

        public static class PartySnapshot
        {
            public const string Code = "code";
            public const string Name = "name";
            public const string Gln = "gln";
        }

        public static class TotalsFields
        {
            public const string InitialAmount = "initialAmount";
            public const string InitialDiscount = "initialDiscount";
            public const string TotalAmount = "totalAmount";
        }

        public static class Item
        {
            public const string ProductCode = "productCode";
            public const string Name = "name";
            public const string Quantity = "quantity";
            public const string UnitPrice = "unitPrice";
            public const string LineDiscount = "lineDiscount";
        }

        public static class ReferencesFields
        {
            public const string DespatchReference = "despatchReference";
            public const string InvoiceReference = "invoiceReference";
            public const string PaymentReference = "paymentReference";
        }

        public static class Event
        {
            public const string EventId = "eventId";
            public const string EventType = "eventType";
            public const string OccurredAt = "occurredAt";
            public const string Summary = "summary";
            public const string Detail = "detail";
            public const string CausationId = "causationId";
        }
    }

    public static IMongoCollection<BsonDocument> Get(IMongoDatabase database) =>
        database.GetCollection<BsonDocument>(Name);
}
