using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>The five thin translation handlers — one RPC call, the reply (or a small slice of it) returned as-is. Ported from #7's own <c>list-stock.query.spec.ts</c>, <c>list-credits.query.spec.ts</c>, <c>list-invoices.query.spec.ts</c>, <c>list-catalog.query.spec.ts</c>, <c>replenish-stock.command.spec.ts</c>.</summary>
public sealed class ReadOnlyQueryHandlerTests
{
    [Fact]
    public async Task ListStockQueryHandler_TranslatesToFulfillmentStockList()
    {
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.StockList, new StockListReplyPayload([], new StockPageInfo(1, 25, 0)));
        var handler = new ListStockQueryHandler(rpc);

        await handler.HandleAsync(new ListStockQuery(1, 25, "IBERFOODS", "PRD-0001", true), CancellationToken.None);

        var call = Assert.Single(rpc.Calls);
        Assert.Equal(GatewaySubjects.StockList, call.Subject);
        var request = Assert.IsType<StockListRequestPayload>(call.Payload);
        Assert.Equal("IBERFOODS", request.CompanyCode);
        Assert.True(request.BelowThreshold);
    }

    [Fact]
    public async Task ListCreditsQueryHandler_TranslatesToBillingCreditList()
    {
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.CreditList, new CreditListReplyPayload([], new CreditPageInfo(1, 25, 0)));
        var handler = new ListCreditsQueryHandler(rpc);

        await handler.HandleAsync(new ListCreditsQuery(1, 25, "CarrefourEs", null), CancellationToken.None);

        var call = Assert.Single(rpc.Calls);
        Assert.Equal(GatewaySubjects.CreditList, call.Subject);
        Assert.Equal("CarrefourEs", Assert.IsType<CreditListRequestPayload>(call.Payload).RetailerCode);
    }

    [Fact]
    public async Task ListInvoicesQueryHandler_TranslatesToBillingInvoiceList_AndPassesThePageThrough()
    {
        var rpc = new FakeRpcClient();
        var reply = new InvoiceListReplyPayload(
            [new InvoiceViewPayload(Guid.NewGuid(), "INV-000027", DateTimeOffset.UtcNow, "ORD-000042", "CarrefourEs", "IBERFOODS", "EUR", 100, 0, 100, "issued", null, null)],
            new InvoicePageInfo(1, 25, 1));
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, reply);
        var handler = new ListInvoicesQueryHandler(rpc);

        var result = await handler.HandleAsync(new ListInvoicesQuery(1, 25, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(GatewaySubjects.InvoiceList, Assert.Single(rpc.Calls).Subject);
        Assert.Equal(1, result.Page.Total);
        Assert.Null(result.Items[0].PaidAt);
    }

    [Fact]
    public async Task ListCatalogQueryHandler_RequestsOnlyTheRequestedKind()
    {
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.CatalogReferenceList, new CatalogReferenceListReplyPayload([], null, null, null));
        var handler = new ListCatalogQueryHandler(rpc);

        await handler.HandleAsync(new ListCatalogQuery(CatalogReferenceKinds.Products, false), CancellationToken.None);

        var request = Assert.IsType<CatalogReferenceListRequestPayload>(Assert.Single(rpc.Calls).Payload);
        Assert.Equal([CatalogReferenceKinds.Products], request.Kinds);
    }

    [Fact]
    public async Task ReplenishStockCommandHandler_TranslatesToFulfillmentStockReplenish_AndPassesTheAffectedItemsThrough()
    {
        var rpc = new FakeRpcClient();
        var items = new[] { new StockViewPayload("IBERFOODS", "PRD-0001", 135, 10, 125, 5) };
        rpc.EnqueueReply(GatewaySubjects.StockReplenish, new StockReplenishReplyPayload(items));
        var handler = new ReplenishStockCommandHandler(rpc);

        var result = await handler.HandleAsync(
            new ReplenishStockCommand("IBERFOODS", [new StockReplenishRequestLine("PRD-0001", 25)]), CancellationToken.None);

        Assert.Equal(GatewaySubjects.StockReplenish, Assert.Single(rpc.Calls).Subject);
        Assert.Same(items, result.Items);
    }
}
