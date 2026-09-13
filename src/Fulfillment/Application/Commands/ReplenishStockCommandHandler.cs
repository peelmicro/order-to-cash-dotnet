using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;

namespace OrderToCash.Fulfillment.Application.Commands;

/// <summary>Thin delegation to <see cref="StockReplenishService.ReplenishAsync"/>.</summary>
public sealed class ReplenishStockCommandHandler(StockReplenishService service) : ICommandHandler<ReplenishStockCommand, StockReplenishReplyPayload>
{
    public Task<StockReplenishReplyPayload> HandleAsync(ReplenishStockCommand command, CancellationToken cancellationToken) =>
        service.ReplenishAsync(command, cancellationToken);
}
