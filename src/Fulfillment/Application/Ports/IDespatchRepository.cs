using OrderToCash.Fulfillment.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// A flat, non-reachable-for-mutation read shape of an already-persisted
/// <see cref="DespatchAdvice"/> — what the F8 fast path (and the F8 in-flight
/// race re-read) needs, never a live aggregate: a despatch is created once
/// and never mutated again.
/// </summary>
public sealed record DespatchSnapshot(
    UniqueId Id,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    OrderNumber OrderReference,
    string CompanyCode,
    string RetailerCode,
    IReadOnlyList<DespatchLineEntry> Lines);

/// <summary>The despatch-side write/read port (mirrors <see cref="IStockItemRepository"/>'s shape). No <c>tx</c> parameter — the ambient transaction comes from the caller's DI scope.</summary>
public interface IDespatchRepository
{
    /// <summary>Non-locking read — the F8 fast path (before any transaction opens) AND the F8 in-flight race re-read (inside the transaction, where it is guaranteed current because both paths lock the same stock rows first). Never locks.</summary>
    Task<DespatchSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>Inserts the despatch header + its lines — a despatch is created ONCE, never updated, so this is always an INSERT, never an upsert (ledger L5) — and drains the aggregate's ONE <c>order.despatched.v1</c> into the outbox. All inside the ambient transaction. Never opens its own (`R13`).</summary>
    Task SaveAsync(DespatchAdvice despatch, CancellationToken cancellationToken);
}
