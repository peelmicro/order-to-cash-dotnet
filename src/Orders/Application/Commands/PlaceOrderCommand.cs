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
/// <paramref name="RequestId"/> is carried and deliberately IGNORED by this
/// feature: <c>requestId</c> idempotent replay ("a repeated <c>orders.create</c>
/// request carrying the same <c>requestId</c> returns the ORIGINAL order")
/// is the reliability feature's own acceptance criterion, out of scope here
/// by the orders_acceptance brief — #7 scoped this out the same way, into
/// its own feature. The field is on the wire because <c>asyncapi.yaml</c>
/// declares it, so the responder reads it onto this command and this
/// handler never looks at it again.
/// </remarks>
public sealed record PlaceOrderCommand(
    Guid? RequestId,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<PlaceOrderRequestLine> Lines,
    long? OrderDiscountMinorUnits,
    string? Notes) : ICommand<PlaceOrderResult>;
