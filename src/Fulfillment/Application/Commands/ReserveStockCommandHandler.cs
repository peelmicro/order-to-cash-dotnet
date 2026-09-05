using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Application.Commands;

/// <summary>Thin delegation to <see cref="StockReservationService.ReserveAsync"/> (design.md §5.1) — the split that keeps the transactional unit a plain class a unit test can <c>new</c> with fakes.</summary>
public sealed class ReserveStockCommandHandler(StockReservationService service) : ICommandHandler<ReserveStockCommand, StockReserveReplyPayload>
{
    public Task<StockReserveReplyPayload> HandleAsync(ReserveStockCommand command, CancellationToken cancellationToken) =>
        service.ReserveAsync(command, cancellationToken);
}
