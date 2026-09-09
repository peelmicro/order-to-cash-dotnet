using System.Text.Json;
using OrderToCash.Contracts.Wire;

namespace OrderToCash.Gateway.Infrastructure.Messaging.Rpc;

/// <summary>
/// Serialises/deserialises RPC request/reply/error payloads through the ONE
/// shared <see cref="JsonWire.Options"/> — the same options every other
/// service's own <c>RpcJson</c> helper uses (CLAUDE.md: "set once in a
/// shared <c>JsonSerializerOptions</c> in <c>Contracts</c> so no service
/// can drift").
/// </summary>
public static class RpcJson
{
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonWire.Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, JsonWire.Options)
        ?? throw new InvalidOperationException($"RPC payload deserialised to null for {typeof(T).Name}.");

    /// <summary>
    /// The <c>RpcError</c> schema's two REQUIRED fields (<c>code</c>,
    /// <c>message</c>) appear together on no success reply payload any
    /// subject this client calls — a cheap, generic discriminator over the
    /// raw JSON that needs no second deserialisation attempt-and-catch.
    /// The identical discriminator Orders' own <c>RpcJson.IsErrorBody</c>
    /// establishes (<c>src/Orders/Infrastructure/Messaging/Rpc/RpcJson.cs</c>).
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
