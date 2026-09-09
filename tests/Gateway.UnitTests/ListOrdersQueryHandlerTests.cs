using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Domain.Projection;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class ListOrdersQueryHandlerTests
{
    private static OrderReadModelDocument CompleteDocument(string reference) => new(
        Guid.NewGuid(), reference, DateTimeOffset.UtcNow,
        new OrderReadModelParty("CarrefourEs", null, "8412345000013"),
        new OrderReadModelParty("IBERFOODS", null, "8412345000020"),
        "placed", null, "EUR", new OrderReadModelTotals(100, 0, 100), [],
        new OrderReadModelReferences(null, null, null), [], true, DateTimeOffset.UtcNow);

    private static OrderReadModelDocument Placeholder() => new(
        Guid.NewGuid(), null, null,
        new OrderReadModelParty(null, null, null), new OrderReadModelParty(null, null, null),
        "placed", null, null, new OrderReadModelTotals(null, null, null), [],
        new OrderReadModelReferences(null, null, null), [], false, DateTimeOffset.UtcNow);

    /// <summary>R54 — served exclusively from the read model, never a write-model client.</summary>
    [Fact]
    public async Task HandleAsync_ReturnsEveryFullyProjectedOrder()
    {
        var readModel = new FakeOrderReadModel();
        readModel.Seed(CompleteDocument("ORD-000001"));
        readModel.Seed(CompleteDocument("ORD-000002"));
        var handler = new ListOrdersQueryHandler(readModel);

        var result = await handler.HandleAsync(new ListOrdersQuery(new OrderListFilter(null, null, null, null, 1, 25)), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    /// <summary>R53 — a placeholder document is filtered out of the list rather than emitted half-filled, but the page total still reflects the raw read-model count (ported from #7's identical trade-off).</summary>
    [Fact]
    public async Task HandleAsync_ExcludesPlaceholderDocuments_ButThePageTotalReflectsTheRawCount()
    {
        var readModel = new FakeOrderReadModel();
        readModel.Seed(CompleteDocument("ORD-000001"));
        readModel.Seed(Placeholder());
        var handler = new ListOrdersQueryHandler(readModel);

        var result = await handler.HandleAsync(new ListOrdersQuery(new OrderListFilter(null, null, null, null, 1, 25)), CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(2, result.Page.Total);
    }
}
