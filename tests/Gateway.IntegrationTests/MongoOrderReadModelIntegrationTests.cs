using MongoDB.Bson;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>R54 — served exclusively from the read model, over a REAL MongoDB, never a mock. Proves the query/filter/sort/pagination behaviour <see cref="MongoOrderReadModelMappingTests"/> (unit, no server) cannot: whether the actual driver's <c>$in</c>/dotted-field filters, sort and skip/limit behave as this class assumes.</summary>
[Collection(MongoCollection.Name)]
public sealed class MongoOrderReadModelIntegrationTests(MongoContainerFixture mongo)
{
    private static BsonDocument OrderDocument(Guid orderId, string reference, string status, string retailerCode, DateTimeOffset orderDate) => new()
    {
        [GatewayReadModelCollection.Fields.Id] = orderId.ToString("D"),
        [GatewayReadModelCollection.Fields.OrderId] = orderId.ToString("D"),
        [GatewayReadModelCollection.Fields.OrderReference] = reference,
        [GatewayReadModelCollection.Fields.OrderDate] = orderDate.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
        [GatewayReadModelCollection.Fields.Retailer] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.PartySnapshot.Code] = retailerCode,
            [GatewayReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Gln] = "8412345000013",
        },
        [GatewayReadModelCollection.Fields.Company] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.PartySnapshot.Code] = "IBERFOODS",
            [GatewayReadModelCollection.Fields.PartySnapshot.Name] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.PartySnapshot.Gln] = "8412345000020",
        },
        [GatewayReadModelCollection.Fields.Status] = status,
        [GatewayReadModelCollection.Fields.CancellationReason] = BsonNull.Value,
        [GatewayReadModelCollection.Fields.Currency] = "EUR",
        [GatewayReadModelCollection.Fields.Totals] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.TotalsFields.InitialAmount] = 100L,
            [GatewayReadModelCollection.Fields.TotalsFields.InitialDiscount] = 0L,
            [GatewayReadModelCollection.Fields.TotalsFields.TotalAmount] = 100L,
        },
        [GatewayReadModelCollection.Fields.Items] = new BsonArray(),
        [GatewayReadModelCollection.Fields.References] = new BsonDocument
        {
            [GatewayReadModelCollection.Fields.ReferencesFields.DespatchReference] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.ReferencesFields.InvoiceReference] = BsonNull.Value,
            [GatewayReadModelCollection.Fields.ReferencesFields.PaymentReference] = BsonNull.Value,
        },
        [GatewayReadModelCollection.Fields.Events] = new BsonArray(),
        [GatewayReadModelCollection.Fields.HeaderComplete] = true,
        [GatewayReadModelCollection.Fields.UpdatedAt] = orderDate.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
        // The three internal-only fields a real projector document also
        // carries — present here so the exclusion projection has something
        // real to exclude, not merely an absent field.
        [GatewayReadModelCollection.Fields.StatusRank] = 5,
        [GatewayReadModelCollection.Fields.TimelineOrderVersion] = 2,
        [GatewayReadModelCollection.Fields.ProcessedEventKeys] = new BsonArray(),
    };

    [Fact]
    public async Task FindByIdAsync_ReturnsTheDocument_MappedFromARealServersStoredBson()
    {
        var collection = mongo.FreshCollection($"otc_read_model_it_{Guid.NewGuid():N}");
        var orderId = Guid.NewGuid();
        // Inserted WITH the three internal-only fields present — a real
        // projector document always carries them — so this proves the
        // production ToDocument/query path tolerates their presence
        // (rather than a fixture that happens never to write them).
        await collection.InsertOneAsync(OrderDocument(orderId, "ORD-000001", "placed", "CarrefourEs", DateTimeOffset.UtcNow));
        var readModel = new MongoOrderReadModel(collection);

        var found = await readModel.FindByIdAsync(orderId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("ORD-000001", found!.OrderReference);
        Assert.Equal("CarrefourEs", found.Retailer.Code);
        Assert.Equal(100, found.Totals.TotalAmount);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNull_ForAnUnknownId()
    {
        var collection = mongo.FreshCollection($"otc_read_model_it_{Guid.NewGuid():N}");
        var readModel = new MongoOrderReadModel(collection);

        Assert.Null(await readModel.FindByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByStatusAndRetailerCode_AndSortsByOrderDateDescending()
    {
        var collection = mongo.FreshCollection($"otc_read_model_it_{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        await collection.InsertManyAsync(
        [
            OrderDocument(Guid.NewGuid(), "ORD-000001", "placed", "CarrefourEs", now.AddMinutes(-10)),
            OrderDocument(Guid.NewGuid(), "ORD-000002", "placed", "CarrefourEs", now),
            OrderDocument(Guid.NewGuid(), "ORD-000003", "cancelled", "CarrefourEs", now.AddMinutes(-5)),
            OrderDocument(Guid.NewGuid(), "ORD-000004", "placed", "OtherRetailer", now.AddMinutes(-2)),
        ]);
        var readModel = new MongoOrderReadModel(collection);

        var result = await readModel.ListAsync(new OrderListFilter(["placed"], "CarrefourEs", null, null, 1, 25), CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(["ORD-000002", "ORD-000001"], result.Items.Select(i => i.OrderReference).ToList());
    }

    [Fact]
    public async Task ListAsync_PaginatesWithSkipAndLimit()
    {
        var collection = mongo.FreshCollection($"otc_read_model_it_{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await collection.InsertOneAsync(OrderDocument(Guid.NewGuid(), $"ORD-00000{i}", "placed", "CarrefourEs", now.AddMinutes(-i)));
        }

        var readModel = new MongoOrderReadModel(collection);

        var page1 = await readModel.ListAsync(new OrderListFilter(null, null, null, null, 1, 2), CancellationToken.None);
        var page2 = await readModel.ListAsync(new OrderListFilter(null, null, null, null, 2, 2), CancellationToken.None);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.OrderReference).Intersect(page2.Items.Select(i => i.OrderReference)));
    }
}
