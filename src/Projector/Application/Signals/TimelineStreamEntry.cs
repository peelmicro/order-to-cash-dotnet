namespace OrderToCash.Projector.Application.Signals;

/// <summary>
/// <c>specs/shared/openapi.yaml</c>'s <c>TimelineStreamEntry</c> shape,
/// published on <c>readmodel.timeline.appended.&lt;orderId&gt;</c> — the
/// required fields are <c>[eventId, orderId, eventType, occurredAt, summary]</c>.
/// Carries <c>causationId</c> (<c>PR33</c> — part of the public read
/// contract) even though it is not in the required set.
/// </summary>
public sealed record TimelineStreamEntry(
    Guid EventId,
    Guid CausationId,
    Guid OrderId,
    string? OrderReference,
    string EventType,
    DateTimeOffset OccurredAt,
    string Summary);
