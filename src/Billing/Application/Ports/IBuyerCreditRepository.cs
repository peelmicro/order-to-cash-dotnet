using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Ports;

/// <summary>The locking write-side port (design.md §5.2). No <c>tx</c> parameter anywhere — the ambient transaction comes from the caller's DI scope, exactly as Orders' <c>IOrderRepository</c> already does.</summary>
public interface IBuyerCreditRepository
{
    /// <summary>
    /// §5.5 steps 1-3: an exclusive lock on the <c>credits</c> row of
    /// <c>(retailerCode, companyCode)</c>; then, under that lock, the `BC5`
    /// committed-exposure scalar and the complete entry list of
    /// <paramref name="orderReference"/>. Returns <see langword="null"/>
    /// when no credit line exists (`BC3`) — the caller turns that into
    /// <c>CreditLineNotFoundError</c>, and no transaction has written
    /// anything.
    /// </summary>
    Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>
    /// Adds <c>credit.AppendedEntries</c> (never an UPDATE, never a DELETE —
    /// `B2`), then drains <c>credit.DomainEvents</c> into outbox rows, then
    /// <c>SaveChangesAsync</c> — all inside the ambient transaction. Never
    /// opens its own (`R13`). The <c>credits</c> row is NEVER written.
    /// </summary>
    Task SaveChangesAsync(BuyerCredit credit, CancellationToken cancellationToken);
}
