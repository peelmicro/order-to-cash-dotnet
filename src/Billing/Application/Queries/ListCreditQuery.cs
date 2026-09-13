using OrderToCash.Billing.Application.Ports;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;

namespace OrderToCash.Billing.Application.Queries;

/// <summary>The <c>billing.credit.list</c> query — a non-locking, non-mutating read, three plain <c>SELECT</c>s.</summary>
public sealed record ListCreditQuery(CreditListRequestPayload Request) : IQuery<CreditListReplyPayload>;

/// <summary>Thin delegation to <see cref="ICreditReadPort.ListAsync"/>.</summary>
public sealed class ListCreditQueryHandler(ICreditReadPort readPort) : IQueryHandler<ListCreditQuery, CreditListReplyPayload>
{
    public Task<CreditListReplyPayload> HandleAsync(ListCreditQuery query, CancellationToken cancellationToken) =>
        readPort.ListAsync(query.Request, cancellationToken);
}
