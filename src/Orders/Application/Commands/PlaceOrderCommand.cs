using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>One requested line of an <c>orders.create</c> request — the wire shape's own optionality preserved: <c>unitPrice</c>/<c>lineDiscount</c> omitted means "snapshot the catalogue price" / "no discount".</summary>
public sealed record PlaceOrderRequestLine(
    string ProductCode,
    Quantity Quantity,
    long? UnitPriceMinorUnits,
    long? LineDiscountMinorUnits);

/// <summary>
/// The <c>orders.create</c> command — <c>asyncapi.yaml</c>
/// <c>OrdersCreateRequestPayload</c>, carried through the dispatcher as-is
/// rather than re-parsed a second time by the handler.
/// </summary>
/// <remarks>
/// <c>RequestId</c> is the idempotency key for <c>orders.create</c> replay
/// (<c>RI1</c>–<c>RI5</c>). The field is on the wire because
/// <c>asyncapi.yaml</c> declares it, and the responder reads it onto this
/// command; <c>orders_acceptance</c> deliberately carried it without acting
/// on it, but feature 27 (<c>orders_reliability</c>) ended that —
/// <c>PlaceOrderCommandHandler.HandleAsync</c> now opens with a
/// <c>FindByRequestIdAsync</c> fast path (<c>RI2</c>) that returns the
/// ORIGINAL order's reply before any reference-data lookup or stock check,
/// and <c>null</c> here means "no idempotency key supplied" (<c>RI4</c>),
/// never "ignore this field".
/// </remarks>
public sealed record PlaceOrderCommand(
    Guid? RequestId,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<PlaceOrderRequestLine> Lines,
    long? OrderDiscountMinorUnits,
    string? Notes) : ICommand<PlaceOrderResult>;
