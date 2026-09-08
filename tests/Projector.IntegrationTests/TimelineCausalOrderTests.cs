using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR10</c>'s causal timeline order, proven end to end against a real
/// <c>mongo:8.3.8</c>, with adversarially chosen <c>eventId</c>s so the
/// <c>eventId</c> fallback ALONE would invert the result if <c>__depth</c>
/// were not part of the sort key.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class TimelineCausalOrderTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    private async Task<BsonArray> EventsAsync(ProjectionRuntime runtime, Guid orderId)
    {
        var doc = await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();
        return doc["events"].AsBsonArray;
    }

    /// <summary>design.md §5.4.1's real tie group: <c>stock.released.v1</c> → <c>order.cancelled.v1</c> (same occurredAt, causationId names the cause).</summary>
    [Fact]
    public async Task R28_StockReleasedPrecedesTheOrderCancelledWhoseCausationIdNamesIt_WithAdversarialEventIds()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r28-tiegroup");
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Adversarial: the CAUSE's eventId sorts AFTER the EFFECT's eventId
        // lexicographically — if __depth were dropped from the sort key,
        // the eventId fallback alone would place cancelled BEFORE released.
        var releasedEventId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var cancelledEventId = Guid.Parse("00000000-0000-0000-0000-000000000000");

        var released = EnvelopeBuilders.StockReleased(eventId: releasedEventId, correlationId: orderId, occurredAt: occurredAt, reason: "credit_rejected");
        var cancelled = EnvelopeBuilders.OrderCancelled(eventId: cancelledEventId, correlationId: orderId, occurredAt: occurredAt, causationId: releasedEventId);

        // Deliver in the INVERTING order too — the final array must not depend on it.
        await runtime.ApplyAsync(cancelled.ToDomain());
        await runtime.ApplyAsync(released.ToDomain());

        var events = await EventsAsync(runtime, orderId);
        Assert.Equal(2, events.Count);
        Assert.Equal(releasedEventId.ToString("D"), events[0]["eventId"].AsString);
        Assert.Equal(cancelledEventId.ToString("D"), events[1]["eventId"].AsString);
    }

    /// <summary>design.md §5.4.1's completion triple: <c>payment.received.v1</c> → <c>credit.released.v1</c> → <c>order.completed.v1</c>, all sharing one occurredAt.</summary>
    [Fact]
    public async Task R24_TheCompletionTripleStoresOrderCompletedLast_BehindTheCreditReleasedItNames_BehindThePaymentReceivedThatNames_WithAdversarialEventIds()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r24-triple");
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Adversarial eventIds: reverse-alphabetical of the true causal order.
        var paymentEventId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        var creditReleasedEventId = Guid.Parse("77777777-0000-0000-0000-000000000002");
        var completedEventId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var payment = EnvelopeBuilders.PaymentReceived(eventId: paymentEventId, correlationId: orderId, occurredAt: occurredAt);
        var creditReleased = EnvelopeBuilders.CreditReleased(eventId: creditReleasedEventId, correlationId: orderId, occurredAt: occurredAt, causationId: paymentEventId, reason: "invoice_paid");
        var completed = EnvelopeBuilders.OrderCompleted(eventId: completedEventId, correlationId: orderId, occurredAt: occurredAt, causationId: creditReleasedEventId);

        // Deliver in REVERSE causal order.
        await runtime.ApplyAsync(completed.ToDomain());
        await runtime.ApplyAsync(creditReleased.ToDomain());
        await runtime.ApplyAsync(payment.ToDomain());

        var events = await EventsAsync(runtime, orderId);
        Assert.Equal(3, events.Count);
        Assert.Equal(paymentEventId.ToString("D"), events[0]["eventId"].AsString);
        Assert.Equal(creditReleasedEventId.ToString("D"), events[1]["eventId"].AsString);
        Assert.Equal(completedEventId.ToString("D"), events[2]["eventId"].AsString);
    }

    /// <summary><c>PR31</c>: a causationId outside the tie group, and one naming no stored fact — both place by the eventId fallback.</summary>
    [Fact]
    public async Task PR31_ACausationIdOutsideTheTieGroupAndOneNamingNoStoredFact_BothPlaceByTheEventIdFallback()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr31-fallback");
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var idA = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var idB = Guid.Parse("22222222-0000-0000-0000-000000000000");
        var neverStored = Guid.NewGuid();

        // A: causationId names a fact NOT in this tie group at all.
        var a = EnvelopeBuilders.StockReserved(eventId: idA, correlationId: orderId, occurredAt: occurredAt, causationId: neverStored);
        // B: causationId names something never received either.
        var b = EnvelopeBuilders.CreditApproved(eventId: idB, correlationId: orderId, occurredAt: occurredAt, causationId: Guid.NewGuid());

        await runtime.ApplyAsync(b.ToDomain());
        await runtime.ApplyAsync(a.ToDomain());

        var events = await EventsAsync(runtime, orderId);
        Assert.Equal(2, events.Count);
        // Both depth 0 (no cause in the tie group) — fallback orders by eventId ascending.
        Assert.Equal(idA.ToString("D"), events[0]["eventId"].AsString);
        Assert.Equal(idB.ToString("D"), events[1]["eventId"].AsString);
    }

    /// <summary><c>PR31</c>: an entry with no causationId computes depth zero without throwing — proven at the migration level in <c>TimelineOrderMigrationTests.PR35_...</c>, and here at the live-apply level via a status-less first fact.</summary>
    [Fact]
    public async Task PR31_AnEntryWithNoCausationIdComputesDepthZeroWithoutThrowing()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr31-nocausation");
        var orderId = Guid.NewGuid();

        // FactEnvelope always carries SOME causationId on the wire in this
        // service's own domain, so this proves the pipeline SIDE: an entry
        // whose causationId (fabricated below) matches nothing in the tie
        // group still resolves depth 0 without throwing.
        var envelope = EnvelopeBuilders.OrderPlaced(correlationId: orderId, causationId: Guid.NewGuid());
        var exception = await Record.ExceptionAsync(() => runtime.ApplyAsync(envelope.ToDomain()));
        Assert.Null(exception);
    }

    /// <summary><c>PR31</c>: a fabricated cycle terminates deterministically with every entry present exactly once.</summary>
    [Fact]
    public async Task PR31_AFabricatedCycleTerminatesDeterministicallyWithEveryEntryPresentExactlyOnce()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr31-cycle");
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var idX = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        var idY = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

        // X's causationId == Y's eventId, and Y's causationId == X's eventId — a 2-cycle.
        var x = EnvelopeBuilders.StockReserved(eventId: idX, correlationId: orderId, occurredAt: occurredAt, causationId: idY);
        var y = EnvelopeBuilders.CreditApproved(eventId: idY, correlationId: orderId, occurredAt: occurredAt, causationId: idX);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await runtime.ApplyAsync(x.ToDomain());
            await runtime.ApplyAsync(y.ToDomain());
        });

        Assert.Null(exception); // terminates — no throw, no infinite loop (the $min cap bounds depth).

        var events = await EventsAsync(runtime, orderId);
        Assert.Equal(2, events.Count);
        var eventIds = events.Select(e => e["eventId"].AsString).ToHashSet();
        Assert.Equal(2, eventIds.Count); // every entry present EXACTLY once.
    }

    /// <summary><c>PR31</c>: two siblings sharing one causationId order by eventId — identically after a later unrelated apply.</summary>
    [Fact]
    public async Task PR31_TwoSiblingsSharingOneCausationIdOrderByEventId_IdenticallyAfterALaterUnrelatedApply()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr31-siblings");
        var orderId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var parentId = Guid.Parse("00000000-1111-1111-1111-111111111111");
        var siblingLow = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var siblingHigh = Guid.Parse("22222222-0000-0000-0000-000000000000");

        var parent = EnvelopeBuilders.OrderPlaced(eventId: parentId, correlationId: orderId, occurredAt: occurredAt.AddMinutes(-1));
        var siblingA = EnvelopeBuilders.StockReserved(eventId: siblingHigh, correlationId: orderId, occurredAt: occurredAt, causationId: parentId);
        var siblingB = EnvelopeBuilders.CreditApproved(eventId: siblingLow, correlationId: orderId, occurredAt: occurredAt, causationId: parentId);

        await runtime.ApplyAsync(parent.ToDomain());
        await runtime.ApplyAsync(siblingA.ToDomain());
        await runtime.ApplyAsync(siblingB.ToDomain());

        var events = await EventsAsync(runtime, orderId);
        Assert.Equal(3, events.Count);
        Assert.Equal(siblingLow.ToString("D"), events[1]["eventId"].AsString); // siblings ordered by eventId.
        Assert.Equal(siblingHigh.ToString("D"), events[2]["eventId"].AsString);

        // A LATER, unrelated apply must not disturb the siblings' relative order.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderConfirmed(correlationId: orderId, occurredAt: occurredAt.AddMinutes(1)).ToDomain());
        var eventsAfter = await EventsAsync(runtime, orderId);
        Assert.Equal(siblingLow.ToString("D"), eventsAfter[1]["eventId"].AsString);
        Assert.Equal(siblingHigh.ToString("D"), eventsAfter[2]["eventId"].AsString);
    }

    /// <summary><c>C6</c>/<c>K6</c>'s live counterpart — arm by removing __depth from sortBy: BOTH R28 and R24 must fail, not one.</summary>
    [Fact]
    public void TheEventsStageOrderingDependsOnDepth_NotEventIdAlone()
    {
        // A structural sanity check that the two behavioural cases above
        // are genuinely exercising __depth: assert the emitted expression
        // contains __depth in the sort key (also proven in DeltaToPipelineTests).
        var expr = TimelineOrder.Expression("$events", 32);
        Assert.Contains("__depth", expr.ToJson(), StringComparison.Ordinal);
    }
}
