using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The <c>orders.create</c> success reply's payload, one field per
/// <c>asyncapi.yaml</c> <c>OrdersCreateReplyPayload</c> required/optional
/// property. <see cref="InitialAmount"/>, <see cref="InitialDiscount"/> and
/// <see cref="TotalAmount"/> are three DISTINCT fields on the wire — #7's
/// own first rejected feature shipped a mapping that silently swapped two
/// of them, undetected because every fixture used a zero discount. This
/// type carries all three separately rather than folding any pair together,
/// so a swap in the responder's wire mapping is a type-correct assignment
/// of the wrong property, not an invisible one.
/// </summary>
public sealed record PlaceOrderResult(
    UniqueId OrderId,
    OrderNumber OrderReference,
    OrderStatus Status,
    string Currency,
    Money InitialAmount,
    Money InitialDiscount,
    Money TotalAmount,
    DateTimeOffset OrderDate);
