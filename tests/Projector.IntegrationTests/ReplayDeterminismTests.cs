using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>design.md §11.2's four-step protocol — whole-document <c>BsonDocument</c> equality, element order included, never a projection of the document.</summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class ReplayDeterminismTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    private static IReadOnlyList<FactEnvelope> OneSagaFactSet(Guid orderId)
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var placed = EnvelopeBuilders.OrderPlaced(correlationId: orderId, occurredAt: t0);
        var reserved = EnvelopeBuilders.StockReserved(correlationId: orderId, occurredAt: t0.AddMinutes(1));
        var creditApproved = EnvelopeBuilders.CreditApproved(correlationId: orderId, occurredAt: t0.AddMinutes(2));
        var confirmed = EnvelopeBuilders.OrderConfirmed(correlationId: orderId, occurredAt: t0.AddMinutes(2), causationId: creditApproved.EventId);
        var despatched = EnvelopeBuilders.OrderDespatched(correlationId: orderId, occurredAt: t0.AddMinutes(3));
        var invoiceIssued = EnvelopeBuilders.InvoiceIssued(correlationId: orderId, occurredAt: t0.AddMinutes(4));
        var paymentReceived = EnvelopeBuilders.PaymentReceived(correlationId: orderId, occurredAt: t0.AddDays(1));
        var creditReleased = EnvelopeBuilders.CreditReleased(correlationId: orderId, occurredAt: t0.AddDays(1).AddSeconds(5), causationId: paymentReceived.EventId, reason: "invoice_paid");
        var completed = EnvelopeBuilders.OrderCompleted(correlationId: orderId, occurredAt: t0.AddDays(1).AddSeconds(5), causationId: creditReleased.EventId);

        return
        [
            placed.ToDomain(), reserved.ToDomain(), creditApproved.ToDomain(), confirmed.ToDomain(),
            despatched.ToDomain(), invoiceIssued.ToDomain(), paymentReceived.ToDomain(),
            creditReleased.ToDomain(), completed.ToDomain(),
        ];
    }

    [Fact]
    public async Task PR15_ReplayingTheSameFactsShuffledAndDuplicatedReproducesABsonIdenticalDocument()
    {
        var orderId = Guid.NewGuid();
        var facts = OneSagaFactSet(orderId);

        // 1. Produce and consume in NATURAL order; snapshot.
        await using (var runtime1 = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr15-natural"))
        {
            foreach (var fact in facts)
            {
                await runtime1.ApplyAsync(fact);
            }

            var firstDoc = await runtime1.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();

            // 2/3. A SEEDED shuffle, each fact duplicated 0/1/2 extra times, across the SAME order id.
            var random = new Random(42);
            var shuffled = facts.OrderBy(_ => random.Next()).ToList();
            var withDuplicates = new List<FactEnvelope>();
            foreach (var fact in shuffled)
            {
                var extra = random.Next(0, 3);
                for (var i = 0; i <= extra; i++)
                {
                    withDuplicates.Add(fact);
                }
            }

            withDuplicates = withDuplicates.OrderBy(_ => random.Next()).ToList();

            await using var runtime2 = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr15-shuffled");
            foreach (var fact in withDuplicates)
            {
                await runtime2.ApplyAsync(fact);
            }

            var secondDoc = await runtime2.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();

            // 4. Whole-document equality, element order included.
            Assert.Equal(firstDoc, secondDoc);
        }
    }

    /// <summary>A fact delivered BEFORE the fact that caused it produces the IDENTICAL final array as the reverse arrival — the whole array is re-sorted in full on every apply, not appended at arrival-time position.</summary>
    [Fact]
    public async Task PR15_AFactDeliveredBeforeTheFactThatCausedItProducesTheIdenticalFinalArrayAsTheReverseArrival()
    {
        var orderId1 = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var released = EnvelopeBuilders.StockReleased(correlationId: orderId1, occurredAt: t0, reason: "credit_rejected");
        var cancelled = EnvelopeBuilders.OrderCancelled(correlationId: orderId1, occurredAt: t0, causationId: released.EventId);

        await using var forward = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr15-forward");
        await forward.ApplyAsync(released.ToDomain());
        await forward.ApplyAsync(cancelled.ToDomain());
        var forwardDoc = await forward.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId1.ToString("D"))).FirstAsync();

        var orderId2 = Guid.NewGuid();
        var releasedR = released with { CorrelationId = orderId2 };
        var cancelledR = cancelled with { CorrelationId = orderId2 };

        await using var reverse = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr15-reverse");
        // The EFFECT arrives BEFORE its own cause.
        await reverse.ApplyAsync(cancelledR.ToDomain());
        await reverse.ApplyAsync(releasedR.ToDomain());
        var reverseDoc = await reverse.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId2.ToString("D"))).FirstAsync();

        // Compare only the events array (the two documents have different _id/orderId by construction).
        Assert.Equal(forwardDoc["events"], reverseDoc["events"]);
    }
}
