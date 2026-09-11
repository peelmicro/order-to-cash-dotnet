using Microsoft.EntityFrameworkCore;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The <c>orders.create</c> command handler (orders_acceptance "What to
/// build"): resolve reference data, call the synchronous stock check
/// BEFORE persisting anything (R31, saga.md §3.1 step 0), and on success
/// <see cref="Order.Place"/> + <see cref="IOrderRepository.AddAsync"/> +
/// <see cref="IOrderRepository.SaveChangesAsync"/> inside one
/// <see cref="IUnitOfWork"/> so the aggregate row and the
/// <c>order.placed.v1</c> outbox record commit together (R13). On a
/// stock-check failure (business rejection OR transport failure/timeout)
/// nothing is persisted and no fact is emitted — the unit of work is never
/// even opened. Mirrors #7's
/// <c>apps/orders/src/application/place-order.handler.ts</c> shape,
/// including feature <c>observability_reliability</c>'s <c>requestId</c>
/// idempotent-replay fast path (<c>RI1</c>–<c>RI5</c>, design.md §2) — no
/// longer out of scope, see <see cref="PlaceOrderCommand"/>'s remarks.
/// </summary>
public sealed class PlaceOrderCommandHandler(
    IUnitOfWork unitOfWork,
    IOrderRepository orders,
    IOrderNumberAllocator orderNumbers,
    IOrderReferenceCatalog referenceCatalog,
    IStockAvailabilityChecker stockAvailability,
    IClock clock) : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        // RI2 — the fast path, BEFORE reference-data resolution and the
        // stock check: a repeated requestId for which a committed order
        // already exists performs NO reference-data lookup and NO stock
        // check, and returns that order's ORIGINAL reply (design.md §2.3).
        if (command.RequestId is { } requestId)
        {
            var existing = await orders.FindByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return ToResult(existing);
            }
        }

        if (command.OrderDiscountMinorUnits is { } orderDiscount && orderDiscount != 0)
        {
            throw new OrderDiscountNotSupportedError(orderDiscount);
        }

        var retailer = await referenceCatalog.FindRetailerAsync(command.RetailerCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ReferenceDataNotFoundError("retailerCode", command.RetailerCode);
        var company = await referenceCatalog.FindCompanyAsync(command.CompanyCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ReferenceDataNotFoundError("companyCode", command.CompanyCode);

        if (!await referenceCatalog.CurrencyExistsAsync(command.Currency, cancellationToken).ConfigureAwait(false))
        {
            throw new ReferenceDataNotFoundError("currency", command.Currency);
        }

        var productCodes = command.Lines.Select(line => line.ProductCode).Distinct(StringComparer.Ordinal).ToArray();
        var products = await referenceCatalog.FindProductsAsync(productCodes, cancellationToken).ConfigureAwait(false);

        foreach (var line in command.Lines)
        {
            if (!products.ContainsKey(line.ProductCode))
            {
                throw new ReferenceDataNotFoundError("productCode", line.ProductCode);
            }
        }

        // The synchronous stock check — BEFORE anything is persisted (R31,
        // saga.md §3.1 step 0). A timeout/transport failure propagates
        // (StockCheckTimeoutError/StockCheckTransportError) and is mapped
        // by the responder's own error mapping, never caught here — no
        // IUnitOfWork is opened on that path either.
        var stockLines = command.Lines
            .Select(line => new StockAvailabilityLine(line.ProductCode, line.Quantity))
            .ToList();
        var stockResult = await stockAvailability.CheckAsync(command.CompanyCode, stockLines, cancellationToken).ConfigureAwait(false);

        if (!stockResult.Available)
        {
            var shortages = stockResult.Lines.Where(line => !line.Sufficient).ToList();
            throw new StockUnavailableError(shortages);
        }

        // Reference data is fully resolved and stock is available — ONLY
        // now is the transaction opened. Order-number allocation happens
        // INSIDE it, so a rollback here also rolls back the allocation
        // rather than burning a sequence number (matching #7's own
        // design note in order-number-allocator.ts, D7 in its review).
        try
        {
            return await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    var orderReference = await orderNumbers.AllocateNextAsync(ct).ConfigureAwait(false);
                    var now = clock.UtcNow;

                    // RI5 — seed causationId from the client's own
                    // requestId when supplied (already a validated,
                    // non-empty Guid off the wire — no parse can fail
                    // here), so the causal chain is reconstructible from
                    // the client's own idempotency key; mint fresh when
                    // omitted, as before this feature.
                    var causationId = command.RequestId is { } seedId ? UniqueId.From(seedId) : UniqueId.New();

                    var orderLines = command.Lines
                        .Select(line => ToOrderLineRequest(line, command.Currency, products[line.ProductCode]))
                        .ToList();

                    var order = Order.Place(
                        orderReference,
                        orderDate: now,
                        retailerCode: command.RetailerCode,
                        buyerGln: retailer.Gln,
                        companyCode: command.CompanyCode,
                        supplierGln: company.Gln,
                        currency: command.Currency,
                        lines: orderLines,
                        notes: command.Notes,
                        occurredAt: now,
                        causationId: causationId);

                    await orders.AddAsync(order, command.RequestId, ct).ConfigureAwait(false);
                    await orders.SaveChangesAsync(ct).ConfigureAwait(false);

                    return ToResult(order);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (command.RequestId is not null && RequestIdCollision.Matches(ex))
        {
            // RI3 — two requests carrying the same, not-yet-committed
            // requestId raced; this one lost. The transaction has ALREADY
            // rolled back by the time this catch runs (design.md §2.4):
            // EfCoreOrderRepository.SaveChangesAsync writes the
            // order.placed.v1 outbox row FIRST, so catching INSIDE
            // unitOfWork.ExecuteAsync and committing would persist a fact
            // for an order that will never exist (ledger L2) — the catch
            // MUST sit outside it, as it does here. Re-read the winner and
            // resolve to ITS reply; never a silent null, never a second
            // order, never an error surfaced to the caller.
            var winner = await orders.FindByRequestIdAsync(command.RequestId.Value, cancellationToken).ConfigureAwait(false);
            if (winner is not null)
            {
                return ToResult(winner);
            }

            // Never a silent null — the collision was real but no winner is
            // readable (e.g. the winner's own transaction has not yet
            // committed as seen from this snapshot). `throw;` rather than
            // `throw ex;` preserves the original stack trace (CA2200).
            throw;
        }
    }

    private static OrderLineRequest ToOrderLineRequest(PlaceOrderRequestLine line, string currency, ProductReference product)
    {
        var unitPrice = line.UnitPriceMinorUnits is { } minorUnits ? new Money(minorUnits, currency) : product.Price;
        var lineDiscount = new Money(line.LineDiscountMinorUnits ?? 0, currency);

        return new OrderLineRequest(line.ProductCode, product.Description, line.Quantity, unitPrice, lineDiscount);
    }

    /// <summary>
    /// The ONE mapping from a persisted <see cref="Order"/> to the reply
    /// shape — used by the normal placement path, the RI2 fast path and the
    /// RI3 re-read, so a repeated request and a first-time request render
    /// identically (ledger L7: no second projection is written for the
    /// replay path).
    /// </summary>
    private static PlaceOrderResult ToResult(Order order) => new(
        order.Id,
        order.OrderReference,
        order.Status,
        order.Currency,
        order.InitialAmount,
        order.InitialDiscount,
        order.TotalAmount,
        order.OrderDate);
}
