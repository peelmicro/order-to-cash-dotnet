using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Queries;

/// <summary><c>GET /catalog/products</c>, <c>GET /catalog/retailers</c>, <c>GET /catalog/companies</c> → NATS RPC <c>catalog.reference.list</c>, one <see cref="CatalogReferenceKinds"/> value at a time.</summary>
public sealed record ListCatalogQuery(string Kind, bool IncludeDisabled) : IQuery<CatalogReferenceListReplyPayload>;

public sealed class ListCatalogQueryHandler(IRpcClient rpc) : IQueryHandler<ListCatalogQuery, CatalogReferenceListReplyPayload>
{
    public Task<CatalogReferenceListReplyPayload> HandleAsync(ListCatalogQuery query, CancellationToken cancellationToken)
    {
        var payload = new CatalogReferenceListRequestPayload([query.Kind], query.IncludeDisabled);
        var requestId = Guid.NewGuid();
        return rpc.CallAsync<CatalogReferenceListRequestPayload, CatalogReferenceListReplyPayload>(
            GatewaySubjects.CatalogReferenceList, payload, new RpcCallMeta(requestId, requestId), cancellationToken);
    }
}
