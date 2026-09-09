namespace OrderToCash.Gateway.Application.Stream;

/// <summary>
/// One `order.updated`/`timeline.appended` frame held by <see cref="StreamHub"/>
/// — ported from #7's <c>application/stream-hub.ts</c> <c>StreamFrame</c>
/// interface. <see cref="DataJson"/> is the exact UTF-8 JSON text the
/// projector's own <c>NatsUpdateSignalPublisher</c> published (never
/// re-decoded then re-encoded — see <c>NatsStreamSignalSubscriber</c>'s own
/// remarks), so it is byte-identical to what the projector wrote, not a
/// structural equivalent produced by a second serialiser.
/// </summary>
public sealed record StreamFrame(string Cursor, string EventType, Guid OrderId, string DataJson);
