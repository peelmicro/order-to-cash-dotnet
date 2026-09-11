namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary><c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload.lines[]</c>.</summary>
public sealed record OrdersCreateRequestLine(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload</c> — the
/// <c>orders.create</c> request body. <see cref="RequestId"/> is read here
/// and carried through to <c>PlaceOrderCommand</c>, whose handler realises
/// feature <c>observability_reliability</c>'s <c>RI1</c>–<c>RI5</c>
/// idempotent-replay behaviour (design.md §2).
/// </summary>
public sealed record OrdersCreateRequestPayload(
    Guid? RequestId,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<OrdersCreateRequestLine> Lines,
    long? OrderDiscount,
    string? Notes);

/// <summary><c>asyncapi.yaml</c> <c>OrdersCreateReplyPayload</c> — the <c>orders.create</c> success reply body. <c>Status</c> is always the literal <c>"placed"</c> (the schema's own <c>const</c>).</summary>
public sealed record OrdersCreateReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    DateTimeOffset OrderDate);
