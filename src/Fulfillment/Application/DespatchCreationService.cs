using OrderToCash.Contracts.Facts;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Application;

/// <summary>
/// The <c>despatch.create</c> transactional unit as a plain class the command
/// handler delegates to — the shape <see cref="StockReservationService"/>
/// already sets, reused rather than reinvented (feature 18 follows feature
/// 17's shapes throughout: the lock protocol, the outbox write, the F8
/// fast-path discipline).
/// </summary>
public sealed class DespatchCreationService(
    IUnitOfWork unitOfWork,
    IStockItemRepository stockRepository,
    IDespatchRepository despatchRepository,
    IDespatchNumberAllocator despatchNumberAllocator,
    IClock clock)
{
    /// <summary>
    /// F8 fast path, then the SAME stock-rows-first lock protocol
    /// <c>stock.release</c> uses (design.md §4.3/§4.4) — the reason two
    /// concurrent <c>despatch.create</c> calls for the same order, or a
    /// <c>despatch.create</c> racing a <c>stock.release</c>, cannot both
    /// proceed: they block on the SAME stock rows.
    /// </summary>
    public async Task<DespatchCreateReplyPayload> CreateAsync(CreateDespatchCommand command, CancellationToken cancellationToken)
    {
        var orderReference = OrderNumber.Parse(command.OrderReference);

        // F8 fast path: a hit returns created: false immediately, NO
        // transaction opened.
        var existing = await despatchRepository.FindByOrderReferenceAsync(orderReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return BuildReply(existing, created: false);
        }

        // The non-locking pre-read (design.md §4.4 step 0's sibling): empty
        // means the order never held a reservation at all — refused BEFORE
        // any transaction opens.
        var lookup = await stockRepository.ProductCodesOfOrderAsync(orderReference, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            throw new NoReservedStockForDespatchError(command.OrderReference);
        }

        return await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var locked = await stockRepository.LockForOrderAsync(lookup.CompanyCode, lookup.ProductCodes, orderReference, ct).ConfigureAwait(false);

                var hasReserved = locked.ExistingReservationsOfOrder.Any(r => r.Status == ReservationStatus.Reserved);
                if (!hasReserved)
                {
                    var hasConsumed = locked.ExistingReservationsOfOrder.Any(r => r.Status == ReservationStatus.Consumed);
                    if (hasConsumed)
                    {
                        // F8 in-flight race: a concurrent committer raced the
                        // fast path and already created the despatch. We now
                        // hold the same stock-row lock IT held, so this
                        // re-read is guaranteed current.
                        var raced = await despatchRepository.FindByOrderReferenceAsync(orderReference, ct).ConfigureAwait(false)
                            ?? throw new ConcurrentDespatchChangeError(command.OrderReference);

                        return BuildReply(raced, created: false);
                    }

                    // None reserved, none consumed (i.e. all released, since
                    // "no reservation row at all" was already handled above).
                    throw new NoReservedStockForDespatchError(command.OrderReference);
                }

                var items = locked.ExistingReservationsOfOrder
                    .Where(r => r.Status == ReservationStatus.Reserved)
                    .Select(r => r.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(code => locked.ItemsByProductCode.TryGetValue(code, out var item) ? item : null)
                    .ToList();

                if (items.Any(item => item is null))
                {
                    throw new ConcurrentDespatchChangeError(command.OrderReference);
                }

                var despatchReference = await despatchNumberAllocator.AllocateNextAsync(ct).ConfigureAwait(false);
                var input = new DespatchOrderInput(orderReference, command.CorrelationId);
                var context = new StockContext(clock.UtcNow, command.RequestId);

                var outcome = OrderDespatch.Create(items!, input, despatchReference, context, UniqueId.New);

                if (outcome.Kind != DespatchOutcomeKind.Created)
                {
                    // Defensive, expected-unreachable (OrderDespatch.Create's
                    // own remark): we already confirmed a `reserved`
                    // reservation exists under this same lock.
                    throw new NoReservedStockForDespatchError(command.OrderReference);
                }

                // StockItem.Consume emits nothing (design.md — order.despatched.v1
                // is this feature's fact, not a stock fact), so this drains
                // the reservation/counter changes only — no stock outbox row.
                await stockRepository.SaveChangesAsync(ct).ConfigureAwait(false);

                // Inserts the despatch header + lines and drains the
                // aggregate's ONE order.despatched.v1 into the outbox.
                await despatchRepository.SaveAsync(outcome.Advice!, ct).ConfigureAwait(false);

                return BuildReplyFromAdvice(outcome.Advice!, created: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static DespatchCreateReplyPayload BuildReply(DespatchSnapshot snapshot, bool created) =>
        new(
            snapshot.OrderReference.Value,
            snapshot.DespatchReference,
            snapshot.DespatchDate,
            created,
            Lines: [.. snapshot.Lines.Select(l => new DespatchLine(l.ProductCode, l.Units.Value))]);

    private static DespatchCreateReplyPayload BuildReplyFromAdvice(DespatchAdvice advice, bool created) =>
        new(
            advice.OrderReference.Value,
            advice.DespatchReference,
            advice.DespatchDate,
            created,
            Lines: [.. advice.Lines.Select(l => new DespatchLine(l.ProductCode, l.Units.Value))]);
}

/// <summary>The <c>fulfillment.despatch.create</c> command — carries <see cref="CorrelationId"/>/<see cref="RequestId"/> as <see cref="UniqueId"/> (mirrors <c>ReserveStockCommand</c>'s `FS3` discipline), extracted from the request's headers by the responder, never from the payload.</summary>
public sealed record CreateDespatchCommand(
    string OrderReference,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<DespatchCreateReplyPayload>;
