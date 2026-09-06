using OrderToCash.Billing.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Billing.Application.Ports;

/// <summary>Never locks, never mutates (`BI15`). Takes the current time as a PARAMETER rather than reading an ambient clock, so the adapter stays a pure query translation.</summary>
public interface IInvoiceReadPort
{
    Task<InvoiceListReplyPayload> ListAsync(InvoiceListRequestPayload query, DateTimeOffset now, CancellationToken cancellationToken);
}
