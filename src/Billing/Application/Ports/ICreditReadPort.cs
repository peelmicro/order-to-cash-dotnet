using OrderToCash.Billing.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Billing.Application.Ports;

/// <summary>The non-locking read side (design.md §5.2) — never locks, never mutates, no transaction.</summary>
public interface ICreditReadPort
{
    Task<CreditListReplyPayload> ListAsync(CreditListRequestPayload query, CancellationToken cancellationToken);
}
