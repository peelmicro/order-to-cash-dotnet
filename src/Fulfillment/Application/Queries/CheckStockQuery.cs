using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Application.Queries;

/// <summary>The <c>fulfillment.stock.check</c> query — a non-locking read (`R31`).</summary>
public sealed record CheckStockQuery(string CompanyCode, IReadOnlyList<StockCheckRequestLine> Lines) : IQuery<StockCheckReplyPayload>;

/// <summary>Thin delegation to <see cref="IStockReadPort.AvailabilityAsync"/> — handlers stay thin, the query unit is a plain class (design.md §5.1's <c>SagaFactHandler</c> shape, Orders).</summary>
public sealed class CheckStockQueryHandler(IStockReadPort readPort) : IQueryHandler<CheckStockQuery, StockCheckReplyPayload>
{
    public Task<StockCheckReplyPayload> HandleAsync(CheckStockQuery query, CancellationToken cancellationToken) =>
        readPort.AvailabilityAsync(query.CompanyCode, query.Lines, cancellationToken);
}
