using MongoDB.Bson;
using OrderToCash.Gateway.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Proves <see cref="MongoOrderReadModel.ToDocument"/> against hand-built
/// <see cref="BsonDocument"/>s shaped exactly as the projector's
/// <c>PlaceholderDocument</c>/<c>DeltaToPipeline</c> write them — no live
/// MongoDB server (<c>MongoDB.Bson</c> has no network dependency), a
/// stronger proof of wire compatibility with the projector's OWN document
/// shape than any fake this project could invent independently, since the
/// field names are transcribed from <c>src/Projector/Infrastructure/Persistence/ReadModelCollection.cs</c>
/// itself.
/// </summary>
public sealed class MongoOrderReadModelMappingTests
{
    private static BsonDocument PlaceholderDocument(Guid orderId, string occurredAtWire) => new()
    {
        [GatewayReadModelCollection.Fields.Id] = orderId.ToString("D"),
        [GatewayReadModelCollection.Fields.OrderId] = orderId.ToString("D"),
        [GatewayReadModelCollection.Fields.OrderReference] = BsonNull.Value,
        [GatewayReadModelCollection.Fields.OrderDate] = BsonNull.Value,
        [GatewayReadModelCollection.Fields.Retailer] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.PartySnapshot.Code] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Gln] = BsonNull.Value,
        },
        [GatewayReadModelCollection.Fields.Company] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.PartySnapshot.Code] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Gln] = BsonNull.Value,
        },
        [GatewayReadModelCollection.Fields.Status] = "placed",
        [GatewayReadModelCollection.Fields.CancellationReason] = BsonNull.Value,
        [GatewayReadModelCollection.Fields.Currency] = BsonNull.Value,
        [GatewayReadModelCollection.Fields.Totals] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.TotalsFields.InitialAmount] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.TotalsFields.InitialDiscount] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.TotalsFields.TotalAmount] = BsonNull.Value,
        },
        [GatewayReadModelCollection.Fields.Items] = new BsonArray(),
        [GatewayReadModelCollection.Fields.References] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.ReferencesFields.DespatchReference] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.ReferencesFields.InvoiceReference] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.ReferencesFields.PaymentReference] = BsonNull.Value,
        },
        [GatewayReadModelCollection.Fields.Events] = new BsonArray(),
        [GatewayReadModelCollection.Fields.HeaderComplete] = false,
        [GatewayReadModelCollection.Fields.UpdatedAt] = occurredAtWire,
    };

    [Fact]
    public void ToDocument_MapsAPlaceholderDocument_WithEveryNullableFieldNull()
    {
        var orderId = Guid.NewGuid();
        var doc = PlaceholderDocument(orderId, "2026-08-18T10:15:00.000Z");

        var mapped = MongoOrderReadModel.ToDocument(doc);

        Assert.Equal(orderId, mapped.OrderId);
        Assert.Null(mapped.OrderReference);
        Assert.Null(mapped.OrderDate);
        Assert.Null(mapped.Retailer.Code);
        Assert.Equal("placed", mapped.Status);
        Assert.False(mapped.HeaderComplete);
        Assert.Empty(mapped.Items);
        Assert.Empty(mapped.Events);
        Assert.Equal(new DateTimeOffset(2026, 8, 18, 10, 15, 0, TimeSpan.Zero), mapped.UpdatedAt);
    }

    [Fact]
    public void ToDocument_MapsAFullyProjectedDocument_IncludingItemsEventsAndReferences()
    {
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var doc = PlaceholderDocument(orderId, "2026-08-18T10:15:00.000Z");

        doc[GatewayReadModelCollection.Fields.OrderReference] = "ORD-000042";
        doc[GatewayReadModelCollection.Fields.OrderDate] = "2026-08-18T10:00:00.000Z";
        doc[GatewayReadModelCollection.Fields.Retailer] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.PartySnapshot.Code] = "CarrefourEs",
            [GatewayReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Gln] = "8412345000013",
        };
        doc[GatewayReadModelCollection.Fields.Currency] = "EUR";
        doc[GatewayReadModelCollection.Fields.Status] = "confirmed";
        doc[GatewayReadModelCollection.Fields.Totals] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.TotalsFields.InitialAmount] = 124250L,
            [GatewayReadModelCollection.Fields.TotalsFields.InitialDiscount] = 0L,
            [GatewayReadModelCollection.Fields.TotalsFields.TotalAmount] = 124250L,
        };
        doc[GatewayReadModelCollection.Fields.Items] = new BsonArray
        {
            new BsonDocument
            {
                [GatewayReadModelCollection.Fields.Item.ProductCode] = "PRD-0001",
                [GatewayReadModelCollection.Fields.Item.Name] = "Widget",
                [GatewayReadModelCollection.Fields.Item.Quantity] = 5,
                [GatewayReadModelCollection.Fields.Item.UnitPrice] = 24850L,
                [GatewayReadModelCollection.Fields.Item.LineDiscount] = 0L,
            },
        };
        doc[GatewayReadModelCollection.Fields.References] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.ReferencesFields.DespatchReference] = "DES-000031",
            [GatewayReadModelCollection.Fields.ReferencesFields.InvoiceReference] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.ReferencesFields.PaymentReference] = BsonNull.Value,
        };
        doc[GatewayReadModelCollection.Fields.Events] = new BsonArray
        {
            new BsonDocument
            {
                [GatewayReadModelCollection.Fields.Event.EventId] = eventId.ToString("D"),
                [GatewayReadModelCollection.Fields.Event.EventType] = "stock.released.v1",
                [GatewayReadModelCollection.Fields.Event.OccurredAt] = "2026-08-18T10:15:04.120Z",
                [GatewayReadModelCollection.Fields.Event.Summary] = "5 units released back to stock",
                [GatewayReadModelCollection.Fields.Event.Detail] = new BsonDocument { ["units"] = 5 },
                [GatewayReadModelCollection.Fields.Event.CausationId] = causationId.ToString("D"),
            },
        };
        doc[GatewayReadModelCollection.Fields.HeaderComplete] = true;

        var mapped = MongoOrderReadModel.ToDocument(doc);

        Assert.Equal("ORD-000042", mapped.OrderReference);
        Assert.Equal("CarrefourEs", mapped.Retailer.Code);
        Assert.Equal("8412345000013", mapped.Retailer.Gln);
        Assert.Equal(124250, mapped.Totals.TotalAmount);
        Assert.Single(mapped.Items);
        Assert.Equal("PRD-0001", mapped.Items[0].ProductCode);
        Assert.Equal("DES-000031", mapped.References.DespatchReference);
        Assert.Single(mapped.Events);
        Assert.Equal(eventId, mapped.Events[0].EventId);
        Assert.Equal(causationId, mapped.Events[0].CausationId);
        Assert.NotNull(mapped.Events[0].Detail);
        Assert.Equal(5, mapped.Events[0].Detail!["units"]);
    }

    [Fact]
    public void ToDocument_LeavesCausationIdNull_WhenTheEntryPredatesTheCausalOrderAmendment()
    {
        var orderId = Guid.NewGuid();
        var doc = PlaceholderDocument(orderId, "2026-08-18T10:15:00.000Z");
        doc[GatewayReadModelCollection.Fields.Events] = new BsonArray
        {
            new BsonDocument
            {
                [GatewayReadModelCollection.Fields.Event.EventId] = Guid.NewGuid().ToString("D"),
                [GatewayReadModelCollection.Fields.Event.EventType] = "order.placed.v1",
                [GatewayReadModelCollection.Fields.Event.OccurredAt] = "2026-08-18T10:15:00.000Z",
                [GatewayReadModelCollection.Fields.Event.Summary] = "Order placed",
                // No `detail`, no `causationId` key at all — a document written before amendment A1.
            },
        };

        var mapped = MongoOrderReadModel.ToDocument(doc);

        Assert.Null(mapped.Events[0].CausationId);
        Assert.Null(mapped.Events[0].Detail);
    }
}
