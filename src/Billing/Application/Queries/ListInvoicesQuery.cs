using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Cqrs;

namespace OrderToCash.Billing.Application.Queries;

/// <summary>The <c>billing.invoice.list</c> query — a non-locking, non-mutating read, two plain <c>SELECT</c>s.</summary>
public sealed record ListInvoicesQuery(InvoiceListRequestPayload Request) : IQuery<InvoiceListReplyPayload>;

/// <summary>Reads <see cref="IClock.UtcNow"/> exactly ONCE and passes it to <see cref="IInvoiceReadPort.ListAsync"/> — the adapter itself never reads an ambient clock (`BI15`, ledger `L28`).</summary>
public sealed class ListInvoicesQueryHandler(IInvoiceReadPort readPort, IClock clock) : IQueryHandler<ListInvoicesQuery, InvoiceListReplyPayload>
{
    public Task<InvoiceListReplyPayload> HandleAsync(ListInvoicesQuery query, CancellationToken cancellationToken) =>
        readPort.ListAsync(query.Request, clock.UtcNow, cancellationToken);
}
