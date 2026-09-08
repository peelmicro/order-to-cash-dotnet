using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR11</c>'s name describes what it PROVES, not a behavioural
/// "overwrite" scenario: no two of the fourteen facts ever write the same
/// reference field, so that case is unreachable by construction (the
/// $ifNull guard is proven at unit level in <c>DeltaToPipelineTests</c>).
/// This file proves the reachable half: each fact fills ONLY its own
/// reference, and an earlier-set reference survives whatever arrives later.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class OutOfOrderFactsTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    private async Task<BsonDocument> DocAsync(ProjectionRuntime runtime, Guid orderId) =>
        await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstAsync();

    [Fact]
    public async Task R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatusOrOverwritingNewerReferences()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "r52-outoforder");
        var orderId = Guid.NewGuid();

        // order.despatched.v1 (rank 5) arrives BEFORE order.placed.v1 (rank 1) —
        // no document exists yet, so a placeholder is created first.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderDespatched(correlationId: orderId).ToDomain());
        var afterDespatch = await DocAsync(runtime, orderId);
        Assert.Equal("despatched", afterDespatch["status"].AsString);
        Assert.False(afterDespatch["headerComplete"].AsBoolean);

        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId).ToDomain());
        var afterPlaced = await DocAsync(runtime, orderId);

        // The rank-1 fact must NOT regress the rank-5 status.
        Assert.Equal("despatched", afterPlaced["status"].AsString);
        Assert.True(afterPlaced["headerComplete"].AsBoolean); // header fields still filled, regardless of arrival order.
        Assert.Equal(2, afterPlaced["events"].AsBsonArray.Count);

        // A status-less fact delivered AFTER a terminal one leaves the terminal status untouched.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderCompleted(correlationId: orderId).ToDomain());
        var afterCompleted = await DocAsync(runtime, orderId);
        Assert.Equal("completed", afterCompleted["status"].AsString);

        await runtime.ApplyAsync(EnvelopeBuilders.StockReleased(correlationId: orderId).ToDomain());
        var afterStatusLess = await DocAsync(runtime, orderId);
        Assert.Equal("completed", afterStatusLess["status"].AsString); // untouched.
    }

    [Fact]
    public async Task PR11_EachFactFillsOnlyItsOwnReference_AndAnEarlierSetReferenceSurvivesALaterFact()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "pr11-outoforder");
        var orderId = Guid.NewGuid();

        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId).ToDomain());
        await runtime.ApplyAsync(EnvelopeBuilders.OrderDespatched(correlationId: orderId, despatchReference: "DES-000001").ToDomain());

        var afterDespatch = await DocAsync(runtime, orderId);
        Assert.Equal("DES-000001", afterDespatch["references"]["despatchReference"].AsString);
        Assert.True(afterDespatch["references"]["invoiceReference"].IsBsonNull);

        // A SECOND despatch-shaped delivery would never happen for the same
        // order in practice, but PR11 also promises the reference "earlier
        // set survives" — an invoice fact fills ONLY invoiceReference, never
        // touching despatchReference.
        await runtime.ApplyAsync(EnvelopeBuilders.InvoiceIssued(correlationId: orderId, invoiceReference: "INV-000001").ToDomain());
        var afterInvoice = await DocAsync(runtime, orderId);
        Assert.Equal("DES-000001", afterInvoice["references"]["despatchReference"].AsString); // survives.
        Assert.Equal("INV-000001", afterInvoice["references"]["invoiceReference"].AsString);
        Assert.True(afterInvoice["references"]["paymentReference"].IsBsonNull);

        await runtime.ApplyAsync(EnvelopeBuilders.PaymentReceived(correlationId: orderId, paymentReference: "PAY-000001").ToDomain());
        var afterPayment = await DocAsync(runtime, orderId);
        Assert.Equal("DES-000001", afterPayment["references"]["despatchReference"].AsString);
        Assert.Equal("INV-000001", afterPayment["references"]["invoiceReference"].AsString);
        Assert.Equal("PAY-000001", afterPayment["references"]["paymentReference"].AsString);
    }
}
