using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;

namespace OrderToCash.Fulfillment.Application.Commands;

/// <summary>Thin delegation to <see cref="StockReservationService.ReleaseAsync"/>.</summary>
public sealed class ReleaseStockCommandHandler(StockReservationService service) : ICommandHandler<ReleaseStockCommand, StockReleaseReplyPayload>
{
    public Task<StockReleaseReplyPayload> HandleAsync(ReleaseStockCommand command, CancellationToken cancellationToken) =>
        service.ReleaseAsync(command, cancellationToken);
}
