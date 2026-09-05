// COPY OF — src/Orders/Infrastructure/Messaging/NatsOptions.cs
namespace OrderToCash.Fulfillment.Infrastructure.Messaging;

/// <summary>The RPC transport's connection settings — <c>asyncapi.yaml</c> <c>servers.rpcTransport</c>: "NATS core request-reply. No durability, no replay, no stream".</summary>
public sealed class NatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";
}
