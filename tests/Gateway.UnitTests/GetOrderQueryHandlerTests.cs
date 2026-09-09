using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Domain.Orders;
using OrderToCash.Gateway.Domain.Projection;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>R54/R55, ported from #7's review finding F3: three-way result, Found/Pending/Unknown.</summary>
public sealed class GetOrderQueryHandlerTests
{
    private static OrderReadModelDocument Document(Guid orderId) => new(
        orderId,
        "ORD-000042",
        DateTimeOffset.UtcNow,
        new OrderReadModelParty("CarrefourEs", "Carrefour", "8412345000013"),
        new OrderReadModelParty("IBERFOODS", "Iberfoods", "8412345000020"),
        "placed",
        null,
        "EUR",
        new OrderReadModelTotals(100, 0, 100),
        [],
        new OrderReadModelReferences(null, null, null),
        [],
        true,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task HandleAsync_ReturnsFound_WhenTheReadModelHasTheDocument()
    {
        var orderId = Guid.NewGuid();
        var readModel = new FakeOrderReadModel();
        readModel.Seed(Document(orderId));
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 10);
        var handler = new GetOrderQueryHandler(readModel, window);

        var result = await handler.HandleAsync(new GetOrderQuery(orderId), CancellationToken.None);

        Assert.Equal(GetOrderResultKind.Found, result.Kind);
        Assert.NotNull(result.Detail);
    }

    /// <summary>An id this gateway recently handed out but the projector has not caught up to yet — 202, never a false 404.</summary>
    [Fact]
    public async Task HandleAsync_ReturnsPending_WhenTheIdWasRecentlyIssuedButNotYetProjected()
    {
        var orderId = Guid.NewGuid();
        var readModel = new FakeOrderReadModel();
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 10);
        window.Record(orderId);
        var handler = new GetOrderQueryHandler(readModel, window);

        var result = await handler.HandleAsync(new GetOrderQuery(orderId), CancellationToken.None);

        Assert.Equal(GetOrderResultKind.Pending, result.Kind);
        Assert.Null(result.Detail);
    }

    /// <summary>An id this gateway never handed out — the genuine 404 case openapi.yaml documents, unreachable before #7's F3 fix.</summary>
    [Fact]
    public async Task HandleAsync_ReturnsUnknown_WhenTheIdWasNeverIssuedByThisGateway()
    {
        var readModel = new FakeOrderReadModel();
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 10);
        var handler = new GetOrderQueryHandler(readModel, window);

        var result = await handler.HandleAsync(new GetOrderQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(GetOrderResultKind.Unknown, result.Kind);
        Assert.Null(result.Detail);
    }
}
