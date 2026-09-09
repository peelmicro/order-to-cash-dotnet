using OrderToCash.Gateway.Domain.Problem;

namespace OrderToCash.Gateway.Application.Ports;

/// <summary>The correlation pair every outbound RPC call carries (asyncapi.yaml <c>RpcHeaders</c>) — the order id when the request concerns a known order, otherwise a fresh gateway request id.</summary>
public readonly record struct RpcCallMeta(Guid CorrelationId, Guid RequestId);

/// <summary>
/// The ONE port every command/query handler that talks to Orders,
/// Fulfillment or Billing uses. One call = one NATS core request-reply
/// round trip. Ported from #7's <c>apps/gateway/src/application/ports/rpc-client.port.ts</c>
/// (<c>RpcClient</c>) — the adapter (<c>Infrastructure/Messaging/NatsRpcClient.cs</c>)
/// is the only place that imports <c>NATS.Client.Core</c> for its value,
/// keeping every application-layer handler free of that dependency.
/// </summary>
public interface IRpcClient
{
    /// <summary>
    /// Resolves with the decoded success payload. Throws
    /// <see cref="RpcTimeoutError"/>, <see cref="RpcTransportError"/> or
    /// <see cref="RpcBusinessError"/> — never resolves with an
    /// <c>RpcError</c>-shaped body; that discrimination happens once, in
    /// the adapter, so every caller can simply <see langword="await"/>.
    /// </summary>
    Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken);
}

/// <summary>Every failure <see cref="IRpcClient.CallAsync{TRequest,TReply}"/> can throw implements <see cref="IRpcErrorLike"/> — so <see cref="RpcErrorClassifier"/> classifies a timeout, a transport failure and a business refusal through the ONE code path, never a special case per exception type.</summary>
public abstract class RpcCallError(string message) : Exception(message), IRpcErrorLike
{
    public abstract string Code { get; }

    /// <summary>Only <see cref="RpcBusinessError"/> carries details; a timeout or a transport failure never does.</summary>
    public virtual IReadOnlyDictionary<string, object?>? Details => null;
}

/// <summary>No reply arrived within the deadline (asyncapi.yaml <c>RpcTimeout</c> — "a legitimate, handled answer", never a message).</summary>
public sealed class RpcTimeoutError(string subject, int timeoutMs) : RpcCallError($"RPC call to \"{subject}\" timed out after {timeoutMs}ms")
{
    public string Subject { get; } = subject;

    public int TimeoutMs { get; } = timeoutMs;

    public override string Code => "TIMEOUT";
}

/// <summary>No responder is subscribed, or the transport itself failed — the command was NOT applied (openapi.yaml <c>UpstreamUnavailable</c>).</summary>
public sealed class RpcTransportError(string subject, string reason) : RpcCallError($"RPC call to \"{subject}\" failed: {reason}")
{
    public string Subject { get; } = subject;

    public override string Code => "UNAVAILABLE";
}

/// <summary>The responder answered with the <c>RpcError</c> reply shape — a business refusal, not a transport failure. Carries the ORIGINAL code/message/details untouched so <see cref="RpcErrorClassifier"/> sees the real value.</summary>
public sealed class RpcBusinessError(string subject, string code, string message, IReadOnlyDictionary<string, object?>? details)
    : RpcCallError(message), IRpcErrorLike
{
    public string Subject { get; } = subject;

    public override string Code { get; } = code;

    public override IReadOnlyDictionary<string, object?>? Details { get; } = details;
}
