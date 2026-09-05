// COPY OF — src/Orders/Infrastructure/Messaging/Rpc/RpcErrorPayload.cs
namespace OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

/// <summary>
/// <c>asyncapi.yaml</c> <c>components.schemas.RpcError</c> — the ONE error
/// reply shape used by every RPC subject. <see cref="Code"/> is one of the
/// twelve closed-enum values; <see cref="Details"/> is untyped ("Shape
/// depends on <c>code</c>").
/// </summary>
public sealed record RpcErrorPayload(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null,
    Guid? CorrelationId = null,
    DateTimeOffset? OccurredAt = null);
