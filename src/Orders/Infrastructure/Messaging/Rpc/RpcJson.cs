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

    /// <summary>
    /// The <c>RpcError</c> schema's two REQUIRED fields (<c>code</c>,
    /// <c>message</c>) appear together on no success reply payload any
    /// outbound RPC client here deserialises — a cheap, generic
    /// discriminator over the raw JSON that needs no second deserialisation
    /// attempt-and-catch. Introduced by feature 42 for
    /// <c>NatsSagaCommandsAdapter</c> and promoted here, shared, by feature
    /// 46 — <c>NatsStockAvailabilityChecker</c> needs the identical check
    /// and a second, independently-drifting copy is exactly the kind of
    /// translation gap the ported-idiom ledger exists to catch.
    /// </summary>
    public static bool IsErrorBody(ReadOnlyMemory<byte> data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("code", out _)
            && root.TryGetProperty("message", out _);
    }
}
