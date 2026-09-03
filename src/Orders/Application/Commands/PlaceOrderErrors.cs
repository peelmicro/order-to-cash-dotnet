using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// Refusals raised by <see cref="PlaceOrderCommandHandler"/> — above the
/// domain (reference data missing, a stock rejection, a wire field the
/// aggregate cannot represent) but before anything is persisted. A
/// genuinely separate hierarchy from <see cref="OrderToCash.SharedKernel.DomainError"/>,
/// matching #7's own split (<c>place-order.errors.ts</c>'s <c>PlaceOrderError</c>
/// is not <c>order-errors.ts</c>'s <c>DomainError</c>) — these are
/// application-layer refusals, not aggregate invariants, and the responder's
/// error mapping (design.md §9.2) checks the specific application types
/// FIRST and only falls back to the generic <c>DomainError</c> catch for an
/// aggregate refusal it does not special-case.
/// </summary>
public abstract class PlaceOrderError(string message) : Exception(message)
{
    public abstract string Code { get; }
}

/// <summary>A <c>retailerCode</c>/<c>companyCode</c>/<c>currency</c>/<c>productCode</c> the request named does not resolve to a known reference row in this context's own catalogue.</summary>
public sealed class ReferenceDataNotFoundError(string field, string value)
    : PlaceOrderError($"{field} \"{value}\" does not resolve to a known reference row.")
{
    public override string Code => "REFERENCE_DATA_NOT_FOUND";

    public string Field { get; } = field;

    public string Value { get; } = value;
}

/// <summary><c>fulfillment.stock.check</c> answered <c>available: false</c> — R31, saga.md §3.1 step 0. Carries the short lines so the RPC error's <c>details</c> can name them (<c>asyncapi.yaml</c>: "STOCK_UNAVAILABLE names the short lines").</summary>
public sealed class StockUnavailableError(IReadOnlyList<StockAvailabilityLineResult> shortages)
    : PlaceOrderError($"Stock check reports {shortages.Count} short line(s): " +
        string.Join(", ", shortages.Select(line => $"{line.ProductCode} (requested {line.Requested}, available {line.Available})")))
{
    public override string Code => "STOCK_UNAVAILABLE";

    public IReadOnlyList<StockAvailabilityLineResult> Shortages { get; } = shortages;
}

/// <summary>
/// <c>OrdersCreateRequestPayload.orderDiscount</c> is part of the wire
/// contract, but <c>orders_aggregate</c> design.md §4.4 deliberately carries
/// no order-level discount field on the aggregate — only per-line
/// discounts. A non-zero value is refused rather than silently dropped
/// (design.md §4.4, inherited from #7's <c>OrderDiscountNotSupportedError</c>).
/// </summary>
public sealed class OrderDiscountNotSupportedError(long orderDiscountMinorUnits)
    : PlaceOrderError($"orderDiscount {orderDiscountMinorUnits} was supplied, but the Order aggregate carries no order-level discount " +
        "(orders_aggregate design.md §4.3/§4.4) — use per-line lineDiscount instead.")
{
    public override string Code => "ORDER_DISCOUNT_NOT_SUPPORTED";

    public long OrderDiscountMinorUnits { get; } = orderDiscountMinorUnits;
}
