using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Queries;

/// <summary><c>GET /credits</c> → NATS RPC <c>billing.credit.list</c>.</summary>
public sealed record ListCreditsQuery(int Page, int PageSize, string? RetailerCode, string? CompanyCode) : IQuery<CreditListReplyPayload>;

public sealed class ListCreditsQueryHandler(IRpcClient rpc) : IQueryHandler<ListCreditsQuery, CreditListReplyPayload>
{
    public Task<CreditListReplyPayload> HandleAsync(ListCreditsQuery query, CancellationToken cancellationToken)
    {
        var payload = new CreditListRequestPayload(query.Page, query.PageSize, query.RetailerCode, query.CompanyCode);
        var requestId = Guid.NewGuid();
        return rpc.CallAsync<CreditListRequestPayload, CreditListReplyPayload>(
            GatewaySubjects.CreditList, payload, new RpcCallMeta(requestId, requestId), cancellationToken);
    }
}
