using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary><c>R53</c> — a fact for an unknown order creates a placeholder, filled in when <c>order.placed.v1</c> arrives.</summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class PlaceholderDocumentIntegrationTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    [Fact]
    public async Task R53_CreatesAPlaceholderDocumentKeyedByCorrelationIdAndFillsInTheHeaderFieldsWhenOrderPlacedIsConsumedLater()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r53-placeholder");
        var orderId = Guid.NewGuid();

        // A fact OTHER than order.placed.v1 arrives first — no document exists yet.
        await runtime.ApplyAsync(EnvelopeBuilders.StockReserved(correlationId: orderId).ToDomain());

        var placeholder = await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
        Assert.Equal(orderId.ToString("D"), placeholder["_id"].AsString);
        Assert.Equal(orderId.ToString("D"), placeholder["orderId"].AsString);
        Assert.False(placeholder["headerComplete"].AsBoolean);
        Assert.True(placeholder["orderReference"].IsBsonNull);
        Assert.Single(placeholder["events"].AsBsonArray);

        // order.placed.v1 is consumed LATER — the header fields are filled in.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId, orderReference: "ORD-000042").ToDomain());

        var complete = await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
        Assert.True(complete["headerComplete"].AsBoolean);
        Assert.Equal("ORD-000042", complete["orderReference"].AsString);
        Assert.Equal(2, complete["events"].AsBsonArray.Count);
    }
}
