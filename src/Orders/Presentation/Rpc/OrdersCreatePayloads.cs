namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary><c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload.lines[]</c>.</summary>
public sealed record OrdersCreateRequestLine(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload</c> — the
/// <c>orders.create</c> request body. <see cref="RequestId"/> is read here
/// because the wire schema declares it, but this feature carries it no
/// further than the responder's own log line — idempotent replay is out of
/// scope (see <c>PlaceOrderCommand</c>'s remarks).
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
