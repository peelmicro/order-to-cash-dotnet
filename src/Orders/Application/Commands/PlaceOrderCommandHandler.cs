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
/// <c>apps/orders/src/application/place-order.handler.ts</c> shape, minus
/// its <c>requestId</c> fast path (out of scope here — see
/// <see cref="PlaceOrderCommand"/>'s remarks).
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
        return await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var orderReference = await orderNumbers.AllocateNextAsync(ct).ConfigureAwait(false);
                var now = clock.UtcNow;
                var causationId = UniqueId.New();

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

                await orders.AddAsync(order, ct).ConfigureAwait(false);
                await orders.SaveChangesAsync(ct).ConfigureAwait(false);

                return new PlaceOrderResult(
                    order.Id,
                    order.OrderReference,
                    order.Status,
                    order.Currency,
                    order.InitialAmount,
                    order.InitialDiscount,
                    order.TotalAmount,
                    order.OrderDate);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static OrderLineRequest ToOrderLineRequest(PlaceOrderRequestLine line, string currency, ProductReference product)
    {
        var unitPrice = line.UnitPriceMinorUnits is { } minorUnits ? new Money(minorUnits, currency) : product.Price;
        var lineDiscount = new Money(line.LineDiscountMinorUnits ?? 0, currency);

        return new OrderLineRequest(line.ProductCode, product.Description, line.Quantity, unitPrice, lineDiscount);
    }
}
