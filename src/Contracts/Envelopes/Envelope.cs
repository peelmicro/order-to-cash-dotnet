namespace OrderToCash.Contracts.Envelopes;

/// <summary>
/// The payload-independent contract carried by every fact
/// (specs/shared/asyncapi.yaml `components.schemas.Envelope`; R11, R12).
/// Every one of the fourteen fact events (`components.schemas.*Event` in the
/// same file) composes this generic envelope with its own payload type — the
/// spec's `allOf: [Envelope, { eventType: const, payload: $ref }]` shape,
/// realised here as <c>Envelope&lt;TPayload&gt;</c> rather than fourteen
/// hand-duplicated envelope wrappers, so the seven envelope fields and their
/// declared order (`eventId`, `eventType`, `aggregateId`, `correlationId`,
/// `causationId`, `occurredAt`, `payload`) exist in exactly one place.
/// </summary>
/// <remarks>
/// The seven properties are declared, in this order, as positional record
/// parameters. `System.Text.Json`'s reflection-based serialiser emits
/// properties in declaration order, which is what makes the envelope
/// byte-exactness assertion possible — see
/// <see cref="OrderToCash.Contracts.Wire.JsonWire"/>. Do not reorder these
/// parameters: the twelve golden envelopes under
/// `tests/Contracts.UnitTests/GoldenEnvelopes/` are asserted against this
/// exact order, and #7's own wire bytes are the oracle for it.
/// </remarks>
/// <typeparam name="TPayload">The fact-specific payload type, e.g. <see cref="OrderToCash.Contracts.Facts.OrderPlacedPayload"/>.</typeparam>
public sealed record Envelope<TPayload>(
    Guid EventId,
    string EventType,
    Guid AggregateId,
    Guid CorrelationId,
    Guid CausationId,
    DateTimeOffset OccurredAt,
    TPayload Payload);
