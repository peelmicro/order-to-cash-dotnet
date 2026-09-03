using System.Text.Json;
using OrderToCash.Contracts.Wire;

namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

/// <summary>
/// Serialises/deserialises RPC request, reply and error payloads to and from
/// raw bytes through the ONE shared <see cref="JsonWire.Options"/> — the
/// same <c>camelCase</c>, nulls-omitted options every Kafka fact uses
/// (CLAUDE.md: "set once in a shared <c>JsonSerializerOptions</c> in
/// <c>Contracts</c> so no service can drift"). An RPC payload is not an
/// <c>Envelope</c> — <c>asyncapi.yaml</c>'s RPC messages carry the payload
/// schema directly, with no envelope wrapper — so this is a thinner helper
/// than the outbox's <c>OutboxEnvelopeMapper</c>, not a copy of it.
/// </summary>
public static class RpcJson
{
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonWire.Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, JsonWire.Options)
        ?? throw new InvalidOperationException($"RPC payload deserialised to null for {typeof(T).Name}.");
}
