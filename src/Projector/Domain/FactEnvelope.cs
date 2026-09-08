namespace OrderToCash.Projector.Domain;

/// <summary>
/// The domain's own input — NOT <c>Contracts.Envelopes.Envelope&lt;TPayload&gt;</c>:
/// that is a wire type parameterised by payload, and the domain must switch
/// on the payload's CLR type without knowing how it arrived (<c>PR28</c>,
/// <c>PR36</c>). <see cref="Payload"/> is one of
/// <c>OrderToCash.Contracts.Facts.Payloads.*</c> — plain records, no
/// serialiser dependency — so this file still carries zero framework
/// references.
/// </summary>
public sealed record FactEnvelope(
    Guid EventId,
    string EventType,
    Guid CorrelationId,
    Guid CausationId,
    DateTimeOffset OccurredAt,
    object Payload);
