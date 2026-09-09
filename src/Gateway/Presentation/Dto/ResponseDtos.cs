namespace OrderToCash.Gateway.Presentation.Dto;

// A handful of response shapes with no matching Application result type —
// everywhere else an Application/Domain result record IS the wire shape
// already (its property names match the openapi.yaml schema field-for-
// field, so it is returned directly and serialised through the app-wide
// JsonWire camelCase options).

public sealed record LoginResponseDto(string AccessToken, string TokenType, int ExpiresIn);

/// <summary>openapi.yaml <c>ProjectionPending</c> — <c>GET /orders/{id}</c>'s 202 body (R55).</summary>
public sealed record ProjectionPendingResponseDto(Guid OrderId, string Status, string Message, int RetryAfterMs);

/// <summary>The <c>{ items: [...] }</c> envelope <c>GET /catalog/products</c>/<c>retailers</c>/<c>companies</c> share.</summary>
public sealed record ItemsResponseDto<T>(IReadOnlyList<T> Items);

/// <summary>openapi.yaml <c>StreamReady</c> — the `data` of the `stream.ready` frame `GET /orders/stream` sends once on connect. <c>OrderId</c> is <see langword="null"/> (omitted on the wire — <c>JsonWire</c>'s nulls-omitted policy) unless the connection is filtered to one order.</summary>
public sealed record StreamReadyResponseDto(string Cursor, bool Resumed, Guid? OrderId);

/// <summary>openapi.yaml <c>StreamPing</c> — the `data` of a `ping` keep-alive frame. Carries no `id:` line on the wire — see <c>StreamEndpoints.WriteFrameAsync</c>'s own remarks for why.</summary>
public sealed record StreamPingResponseDto(DateTimeOffset At);
