using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>The non-locking read side (design.md §5.2) — never locks, never mutates, no transaction.</summary>
public interface IStockReadPort
{
    Task<StockCheckReplyPayload> AvailabilityAsync(string companyCode, IReadOnlyList<StockCheckRequestLine> lines, CancellationToken cancellationToken);

    Task<StockListReplyPayload> ListAsync(StockListRequestPayload query, CancellationToken cancellationToken);
}
