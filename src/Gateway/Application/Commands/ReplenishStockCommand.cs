using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Commands;

/// <summary><c>POST /stock/replenish</c> → NATS RPC <c>fulfillment.stock.replenish</c>. Emits no fact and advances no order — a deliberately plain request/reply translation, no correlationId semantics beyond "this gateway request".</summary>
public sealed record ReplenishStockCommand(string CompanyCode, IReadOnlyList<StockReplenishRequestLine> Lines) : ICommand<StockReplenishReplyPayload>;

public sealed class ReplenishStockCommandHandler(IRpcClient rpc) : ICommandHandler<ReplenishStockCommand, StockReplenishReplyPayload>
{
    public Task<StockReplenishReplyPayload> HandleAsync(ReplenishStockCommand command, CancellationToken cancellationToken)
    {
        var payload = new StockReplenishRequestPayload(command.CompanyCode, command.Lines);
        var requestId = Guid.NewGuid();
        return rpc.CallAsync<StockReplenishRequestPayload, StockReplenishReplyPayload>(
            GatewaySubjects.StockReplenish, payload, new RpcCallMeta(requestId, requestId), cancellationToken);
    }
}
