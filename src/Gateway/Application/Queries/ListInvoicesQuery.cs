using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Queries;

/// <summary><c>GET /invoices</c> → NATS RPC <c>billing.invoice.list</c>.</summary>
public sealed record ListInvoicesQuery(
    int Page,
    int PageSize,
    string? Status,
    string? RetailerCode,
    string? CompanyCode,
    string? OrderReference,
    int? IssuedBeforeMinutes) : IQuery<GatewayInvoiceListReplyPayload>;

public sealed class ListInvoicesQueryHandler(IRpcClient rpc) : IQueryHandler<ListInvoicesQuery, GatewayInvoiceListReplyPayload>
{
    public Task<GatewayInvoiceListReplyPayload> HandleAsync(ListInvoicesQuery query, CancellationToken cancellationToken)
    {
        var payload = new InvoiceListRequestPayload(
            query.Page, query.PageSize, query.Status, query.RetailerCode, query.CompanyCode, query.OrderReference, query.IssuedBeforeMinutes);
        var requestId = Guid.NewGuid();
        return rpc.CallAsync<InvoiceListRequestPayload, GatewayInvoiceListReplyPayload>(
            GatewaySubjects.InvoiceList, payload, new RpcCallMeta(requestId, requestId), cancellationToken);
    }
}
