using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

[Collection(ProjectorInfraCollection.Name)]
public sealed class TimelineProjectionTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    private async Task<BsonDocument> DocAsync(ProjectionRuntime runtime, Guid orderId) =>
        await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();

    [Fact]
    public async Task R50_AppendsAnEntryCarryingEventIdEventTypeOccurredAtAndASummary_AndPresentsTheTimelineOrderedByOccurredAtRatherThanByArrival()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r50-order");
        var orderId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var placed = EnvelopeBuilders.OrderPlaced(correlationId: orderId, occurredAt: t0);
        var confirmed = EnvelopeBuilders.OrderConfirmed(correlationId: orderId, occurredAt: t0.AddMinutes(5));

        // Deliver OUT of occurredAt order — confirmed arrives first.
        await runtime.ApplyAsync(confirmed.ToDomain());
        await runtime.ApplyAsync(placed.ToDomain());

        var doc = await DocAsync(runtime, orderId);
        var events = doc["events"].AsBsonArray;
        Assert.Equal(2, events.Count);
        Assert.Equal("order.placed.v1", events[0]["eventType"].AsString); // by occurredAt, not arrival.
        Assert.Equal("order.confirmed.v1", events[1]["eventType"].AsString);
        Assert.Equal(placed.EventId.ToString("D"), events[0]["eventId"].AsString);
        Assert.False(string.IsNullOrEmpty(events[0]["summary"].AsString));
    }

    /// <summary><c>R51</c>: byte-identical before/after a redelivery, and the writer must report Duplicate (N10).</summary>
    [Fact]
    public async Task R51_LeavesTheReadModelDocumentUnchangedWhenAFactWithAnAlreadyPresentEventIdIsRedelivered()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r51-redeliver");
        var orderId = Guid.NewGuid();
        var envelope = EnvelopeBuilders.OrderPlaced(correlationId: orderId);

        await runtime.ApplyAsync(envelope.ToDomain());
        var before = await DocAsync(runtime, orderId);

        var idempotentConsumer = new OrderToCash.Projector.Infrastructure.Messaging.IdempotentConsumer(runtime.Collection);
        var writer = new MongoReadModelWriter(runtime.Collection, idempotentConsumer);
        var delta = OrderToCash.Projector.Domain.FactProjection.Project(envelope.ToDomain());
        var callbackCount = 0;
        var outcome = await writer.ApplyAsync(delta, envelope.EventId, (_, _) => { callbackCount++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(OrderToCash.Projector.Application.Ports.ProjectionOutcome.Duplicate, outcome);
        Assert.Equal(0, callbackCount);

        var after = await DocAsync(runtime, orderId);
        Assert.Equal(before, after);
    }

    /// <summary><c>PR14</c>: an OLDER fact delivered last must not move <c>updatedAt</c> backwards.</summary>
    [Fact]
    public async Task PR14_UpdatedAtIsTheGreatestOccurredAtApplied_NotTheLatestArrival()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr14-updatedat");
        var orderId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId, occurredAt: t0.AddMinutes(10)).ToDomain());
        var afterNewer = await DocAsync(runtime, orderId);
        Assert.Equal("2026-01-01T00:10:00.000Z", afterNewer["updatedAt"].AsString);

        // An OLDER fact, delivered LAST.
        await runtime.ApplyAsync(EnvelopeBuilders.StockReserved(correlationId: orderId, occurredAt: t0).ToDomain());
        var afterOlder = await DocAsync(runtime, orderId);
        Assert.Equal("2026-01-01T00:10:00.000Z", afterOlder["updatedAt"].AsString); // unchanged.
    }
}
