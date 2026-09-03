using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>One requested line of the synchronous <c>fulfillment.stock.check</c> read.</summary>
public sealed record StockAvailabilityLine(string ProductCode, Quantity Quantity);

/// <summary>One answered line — <c>asyncapi.yaml</c> <c>StockCheckReplyPayload.lines[]</c>.</summary>
public sealed record StockAvailabilityLineResult(string ProductCode, int Requested, int Available, bool Sufficient);

/// <summary><c>available</c> is true only when every line is <c>sufficient</c> — <c>asyncapi.yaml</c> <c>StockCheckReplyPayload</c>.</summary>
public sealed record StockAvailabilityResult(bool Available, IReadOnlyList<StockAvailabilityLineResult> Lines);

/// <summary>
/// The outbound <c>fulfillment.stock.check</c> RPC port — R31, saga.md §2:
/// "<c>stock.check</c> | Orders (acceptance, NOT the saga) | Fulfillment |
/// — (read-only) | per-line available / insufficient". A non-locking read:
/// the adapter mutates nothing and the check itself emits no fact.
/// </summary>
/// <remarks>
/// Per the Kafka-vs-NATS decision matrix (saga.md §1: "If the peer is down
/// — the caller gets a timeout and handles it"), a timeout or any other
/// transport failure is a DISTINCT, explicitly-typed outcome from a
/// BUSINESS rejection (<c>available: false</c>, some line short) — the
/// caller (<c>PlaceOrderCommandHandler</c>) must be able to tell
/// "Fulfillment said no" apart from "Fulfillment did not answer in time",
/// because only the former is a <c>STOCK_UNAVAILABLE</c> RpcError and the
/// latter is a <c>TIMEOUT</c>/<c>UNAVAILABLE</c> one (<c>asyncapi.yaml</c>
/// <c>RpcError.code</c>). Mirrors #7's
/// <c>apps/orders/src/application/ports/stock-availability.port.ts</c>
/// exactly, including the two transport-error types below being plain
/// exceptions rather than <see cref="OrderToCash.SharedKernel.DomainError"/>
/// subtypes — they are not a business refusal with a stable domain code,
/// they are the transport failing to answer at all.
/// </remarks>
public interface IStockAvailabilityChecker
{
    /// <summary>Never throws for a business rejection — <c>available: false</c> IS the answer. Throws only <see cref="StockCheckTimeoutError"/>/<see cref="StockCheckTransportError"/> for a transport-level failure.</summary>
    Task<StockAvailabilityResult> CheckAsync(string companyCode, IReadOnlyList<StockAvailabilityLine> lines, CancellationToken cancellationToken);
}

/// <summary>The caller observed no reply within its deadline — saga.md's "a timeout is a legitimate, handled answer", applied at order acceptance rather than inside the saga (this call happens before the order — and therefore the saga — exists).</summary>
public sealed class StockCheckTimeoutError(string subject, int timeoutMs)
    : Exception($"fulfillment.stock.check: no reply within {timeoutMs}ms on subject \"{subject}\".")
{
    public string Subject { get; } = subject;

    public int TimeoutMs { get; } = timeoutMs;
}

/// <summary>Any other transport-level failure — no responder subscribed (NATS "no responders"), a malformed reply, a connection error. Distinct from a timeout because it is diagnosable immediately rather than after waiting out the deadline.</summary>
public sealed class StockCheckTransportError(string subject, string reason)
    : Exception($"fulfillment.stock.check: transport failure on subject \"{subject}\": {reason}")
{
    public string Subject { get; } = subject;
}
