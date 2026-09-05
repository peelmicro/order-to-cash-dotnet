using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Application;

/// <summary>The reserve/release transactional unit as a plain class the command handlers delegate to (design.md §5.3) — a <c>new</c>-able class a unit test can drive with fakes, no dispatcher, no host.</summary>
public sealed class StockReservationService(
    IUnitOfWork unitOfWork,
    IStockItemRepository repository,
    IClock clock)
{
    /// <summary>
    /// design.md §4.3/§5.3: lock the order's stock rows and its existing
    /// reservations in ONE call to <see cref="IStockItemRepository.LockForOrderAsync"/>
    /// — the <c>already_reserved</c> short-circuit is decided BEFORE any
    /// domain call and BEFORE anything is saved (`FS5`, the exact branch #7
    /// shipped untested and was rejected for). The reply is built from the
    /// domain outcome inside the transactional delegate but returned only
    /// after <see cref="IUnitOfWork.ExecuteAsync{T}"/> resolves, so a
    /// rollback can never have produced a success reply.
    /// </summary>
    public Task<StockReserveReplyPayload> ReserveAsync(ReserveStockCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(
            async ct =>
            {
                var productCodes = command.Lines.Select(line => line.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var orderReference = OrderNumber.Parse(command.OrderReference);

                var locked = await repository.LockForOrderAsync(command.CompanyCode, productCodes, orderReference, ct).ConfigureAwait(false);

                // FS5: the short-circuit filters on NOTHING but orderReference
                // — ANY reservation rows for the order, in ANY status, mean
                // "already handled". No domain call, nothing saved. This is
                // #7's rejected defect (D1), reproduced deliberately and
                // guarded (C7/G4 arming).
                if (locked.ExistingReservationsOfOrder.Count > 0)
                {
                    return BuildAlreadyReservedReply(command.OrderReference, locked.ExistingReservationsOfOrder);
                }

                var input = new ReserveOrderInput(
                    orderReference,
                    command.CompanyCode,
                    command.RetailerCode,
                    [.. command.Lines.Select(line => new ReserveOrderLine(line.ProductCode, new Quantity(line.Units)))],
                    command.CorrelationId);

                var context = new StockContext(clock.UtcNow, command.RequestId);

                var outcome = OrderStockReservation.Reserve(locked.ItemsByProductCode, input, context, UniqueId.New);

                if (outcome.Kind == ReserveOutcomeKind.NoCarrier)
                {
                    throw new NoKnownStockItemError(command.CompanyCode);
                }

                await repository.SaveChangesAsync(ct).ConfigureAwait(false);

                return outcome.Kind == ReserveOutcomeKind.Reserved
                    ? new StockReserveReplyPayload(
                        "accepted",
                        command.OrderReference,
                        Reservations: [.. outcome.ReservedFact!.Reservations])
                    : new StockReserveReplyPayload(
                        "rejected",
                        command.OrderReference,
                        Shortages: [.. outcome.RejectedFact!.Shortages]);
            },
            cancellationToken);

    /// <summary>
    /// design.md §4.4: a non-locking pre-read decides ONLY whether to open a
    /// transaction (`FS9` — no reservation at all is a success no-op with NO
    /// transaction). The authoritative decision is re-made under lock inside
    /// the transaction.
    /// </summary>
    public async Task<StockReleaseReplyPayload> ReleaseAsync(ReleaseStockCommand command, CancellationToken cancellationToken)
    {
        var orderReference = OrderNumber.Parse(command.OrderReference);
        var lookup = await repository.ProductCodesOfOrderAsync(orderReference, cancellationToken).ConfigureAwait(false);

        if (lookup is null)
        {
            return new StockReleaseReplyPayload("already_released", command.OrderReference, Released: []);
        }

        return await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var locked = await repository.LockForOrderAsync(lookup.CompanyCode, lookup.ProductCodes, orderReference, ct).ConfigureAwait(false);

                var items = locked.ExistingReservationsOfOrder
                    .Select(r => r.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(code => locked.ItemsByProductCode.TryGetValue(code, out var item) ? item : null)
                    .ToList();

                if (items.Any(item => item is null))
                {
                    throw new ConcurrentReservationChangeError(command.OrderReference);
                }

                var input = new ReleaseOrderInput(orderReference, command.Reason, command.CorrelationId);
                var context = new StockContext(clock.UtcNow, command.RequestId);

                var outcome = OrderStockReservation.Release(items!, input, context);

                if (outcome.Kind == ReleaseOutcomeKind.AlreadyReleased)
                {
                    return new StockReleaseReplyPayload("already_released", command.OrderReference, Released: []);
                }

                await repository.SaveChangesAsync(ct).ConfigureAwait(false);

                return new StockReleaseReplyPayload(
                    "released",
                    command.OrderReference,
                    Released: [.. outcome.Fact!.Released]);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static StockReserveReplyPayload BuildAlreadyReservedReply(string orderReference, IReadOnlyList<ReservationSnapshot> existing) =>
        new(
            "already_reserved",
            orderReference,
            Reservations: [.. existing.Select(r => new Contracts.Facts.ReservationRef(r.Id.Value, r.ProductCode, r.Units))]);
}

/// <summary>The <c>fulfillment.stock.reserve</c> command — carries <see cref="CorrelationId"/>/<see cref="RequestId"/> as <see cref="UniqueId"/> (`FS3`), extracted from the request's headers by the responder, never from the payload.</summary>
public sealed record ReserveStockCommand(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    IReadOnlyList<StockReserveRequestLine> Lines,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<StockReserveReplyPayload>;

/// <summary>The <c>fulfillment.stock.release</c> command. Carries no <c>companyCode</c> — <c>asyncapi.yaml</c>'s <c>StockReleaseRequestPayload</c> has none; the service reads it from the order's own persisted reservations (<see cref="Ports.IStockItemRepository.ProductCodesOfOrderAsync"/>).</summary>
public sealed record ReleaseStockCommand(
    string OrderReference,
    string Reason,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<StockReleaseReplyPayload>;
