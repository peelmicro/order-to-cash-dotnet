namespace OrderToCash.Gateway.Infrastructure.Messaging.Rpc;

/// <summary><c>asyncapi.yaml</c> <c>components.schemas.RpcError</c> — the ONE error reply shape every RPC subject answers with. The Gateway's own copy of the record every other service's <c>RpcErrorPayload</c> also declares.</summary>
public sealed record RpcErrorPayload(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null, Guid? CorrelationId = null, DateTimeOffset? OccurredAt = null);
