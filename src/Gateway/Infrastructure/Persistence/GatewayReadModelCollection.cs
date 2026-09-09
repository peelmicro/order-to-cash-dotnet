namespace OrderToCash.Gateway.Infrastructure.Persistence;

/// <summary>
/// The <c>otc_read_model.order_timeline</c> collection name and every
/// camelCase element-name constant the projector's document carries —
/// transcribed from <c>src/Projector/Infrastructure/Persistence/ReadModelCollection.cs</c>,
/// not referenced ("the only shared runtime code is
/// SharedKernel/Contracts/Cqrs", CLAUDE.md — the Gateway must not
/// reference the Projector project). This is a DIRECT, READ-ONLY client
/// of another service's collection (R54's own exception: "served
/// exclusively from the projected read model", the ONE place outside the
/// projector this repository reads Mongo from), so the two copies staying
/// byte-identical is a genuine cross-service coupling the guard tests in
/// <c>Gateway.UnitTests</c> assert against.
/// </summary>
public static class GatewayReadModelCollection
{
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
}
