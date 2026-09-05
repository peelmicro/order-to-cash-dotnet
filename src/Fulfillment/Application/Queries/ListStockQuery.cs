using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Application.Queries;

/// <summary>The <c>fulfillment.stock.list</c> query — a non-locking, non-mutating read (`FS15`).</summary>
public sealed record ListStockQuery(StockListRequestPayload Request) : IQuery<StockListReplyPayload>;

/// <summary>Thin delegation to <see cref="IStockReadPort.ListAsync"/>.</summary>
public sealed class ListStockQueryHandler(IStockReadPort readPort) : IQueryHandler<ListStockQuery, StockListReplyPayload>
{
    public Task<StockListReplyPayload> HandleAsync(ListStockQuery query, CancellationToken cancellationToken) =>
        readPort.ListAsync(query.Request, cancellationToken);
}
