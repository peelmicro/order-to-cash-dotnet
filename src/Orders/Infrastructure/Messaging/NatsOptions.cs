namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>
/// The RPC transport's connection settings — <c>asyncapi.yaml</c>
/// <c>servers.rpcTransport</c>: "NATS core request-reply. No durability, no
/// replay, no stream". <see cref="Url"/> matches
/// <c>docker-compose.infra.yml</c>'s <c>nats</c> service (port 4222) by
/// default.
/// </summary>
public sealed class NatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>The caller's budget for <c>fulfillment.stock.check</c> — <c>asyncapi.yaml</c> <c>RpcHeaders.x-deadline-ms</c>'s own vocabulary, applied here as the NATS subscription timeout rather than propagated as a header (§ this feature's own scope note: full <c>traceparent</c>/deadline header propagation is feature 27's, not this one's).</summary>
    public int StockCheckTimeoutMs { get; set; } = 5_000;
}
