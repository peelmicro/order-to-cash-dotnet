// COPY OF — src/Orders/Infrastructure/Messaging/Rpc/RpcJson.cs
using System.Text.Json;
using OrderToCash.Contracts.Wire;

namespace OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

/// <summary>
/// Serialises/deserialises RPC request, reply and error payloads to and from
/// raw bytes through the ONE shared <see cref="JsonWire.Options"/> — the
/// same <c>camelCase</c>, nulls-omitted options every Kafka fact uses.
/// </summary>
public static class RpcJson
{
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonWire.Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, JsonWire.Options)
        ?? throw new InvalidOperationException($"RPC payload deserialised to null for {typeof(T).Name}.");
}
