namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

/// <summary><c>asyncapi.yaml</c> <c>StockCheckRequestPayload.lines[]</c>.</summary>
public sealed record StockCheckRequestLine(string ProductCode, int Quantity);

/// <summary><c>asyncapi.yaml</c> <c>StockCheckRequestPayload</c> — the <c>fulfillment.stock.check</c> request body.</summary>
public sealed record StockCheckRequestPayload(string CompanyCode, IReadOnlyList<StockCheckRequestLine> Lines);

/// <summary><c>asyncapi.yaml</c> <c>StockCheckReplyPayload.lines[]</c>.</summary>
public sealed record StockCheckReplyLine(string ProductCode, int Requested, int Available, bool Sufficient);

/// <summary><c>asyncapi.yaml</c> <c>StockCheckReplyPayload</c> — the <c>fulfillment.stock.check</c> success reply body.</summary>
public sealed record StockCheckReplyPayload(bool Available, IReadOnlyList<StockCheckReplyLine> Lines);
