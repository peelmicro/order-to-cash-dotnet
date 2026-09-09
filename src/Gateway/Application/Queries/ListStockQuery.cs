using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Queries;

/// <summary><c>GET /stock</c> → NATS RPC <c>fulfillment.stock.list</c> — "a live read of the Fulfillment write model, not the read model: stock is not part of an order's timeline" (openapi.yaml's own words). The one list endpoint the contract itself says is NOT R54's read-model rule.</summary>
public sealed record ListStockQuery(int Page, int PageSize, string? CompanyCode, string? ProductCode, bool BelowThreshold) : IQuery<StockListReplyPayload>;

public sealed class ListStockQueryHandler(IRpcClient rpc) : IQueryHandler<ListStockQuery, StockListReplyPayload>
{
    public Task<StockListReplyPayload> HandleAsync(ListStockQuery query, CancellationToken cancellationToken)
    {
        var payload = new StockListRequestPayload(query.Page, query.PageSize, query.CompanyCode, query.ProductCode, query.BelowThreshold);
        var requestId = Guid.NewGuid();
        return rpc.CallAsync<StockListRequestPayload, StockListReplyPayload>(
            GatewaySubjects.StockList, payload, new RpcCallMeta(requestId, requestId), cancellationToken);
    }
}
