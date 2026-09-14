namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The Application-level DTO one consumed fact becomes, once
/// <c>SagaFactsConsumer</c> (Presentation) has parsed and validated its
/// envelope (design.md §3.5). <see cref="Payload"/> is always the
/// <c>Contracts.Facts.FactCatalog</c> CLR type declared for
/// <see cref="EventType"/> — the two step-table rows that read it
/// (<c>stock.released.v1</c>'s two branches) use a C# type pattern
/// (<c>fact.Payload is StockReleasedPayload p</c>) rather than a generic
/// <c>SagaFact&lt;TPayload&gt;</c>, which would force fourteen closed types
/// through a non-generic routing map for no behavioural gain.
/// </summary>
/// <remarks>
/// Pure — no <c>Microsoft.*</c>, <c>Confluent.*</c>, <c>NATS.*</c>, EF Core
/// or <c>System.Text.Json</c> reference anywhere in this file or its
/// neighbours in <c>Application/Sagas/</c> (design.md §4.1). <c>Payload</c>
/// is declared <see cref="object"/> rather than a
/// <c>Contracts.Facts.Payloads.*</c> type so this Application-layer file
/// need not reference <c>OrderToCash.Contracts</c> at all — only
/// <c>Infrastructure/</c> (the consumer that builds a <see cref="SagaFact"/>)
/// and the step table's own pattern matches ever see the concrete payload
/// type.
/// </remarks>
/// <param name="EventId">The fact's own <c>eventId</c>, copied verbatim from the envelope — the dedup key every saga step is run under (R18).</param>
/// <param name="EventType">The envelope's <c>eventType</c> (<c>&lt;aggregate&gt;.&lt;fact&gt;.v&lt;n&gt;</c>), the step table's routing key.</param>
/// <param name="AggregateId">The envelope's <c>aggregateId</c> — the producing aggregate's id, NOT necessarily the order's.</param>
/// <param name="CorrelationId">The envelope's <c>correlationId</c> — always the ORDER id across this saga (saga.md's invariant).</param>
/// <param name="CausationId">The envelope's <c>causationId</c> — the event or request that caused this one.</param>
/// <param name="OccurredAt">The envelope's <c>occurredAt</c>, UTC, never re-stamped by the consumer.</param>
/// <param name="Payload">The <c>Contracts.Facts.FactCatalog</c> CLR type declared for <paramref name="EventType"/>, declared <see cref="object"/> here so this Application-layer file references no Contracts type.</param>
/// <param name="TriggeringEventEnvelope">
/// Feature <c>observability_reliability</c>, <c>OR3</c>/<c>R29</c>'s
/// dead-letter clause (design.md §4.2) — the RAW bytes of this fact's own
/// envelope, exactly as <c>SagaFactsConsumer</c> received them, threaded
/// through unmodified to <c>ISagaCommandStore.EnqueueAsync</c> so a later
/// first-park can republish the ORIGINAL bytes byte-for-byte. Defaulted to
/// <see langword="null"/> (not required) so tests that do not care about
/// dead-lettering are not forced to supply it.
/// </param>
/// <param name="TriggeringEventTopic">The source topic <paramref name="TriggeringEventEnvelope"/> was consumed from.</param>
public sealed record SagaFact(
    Guid EventId,
    string EventType,
    Guid AggregateId,
    Guid CorrelationId,
    Guid CausationId,
    DateTimeOffset OccurredAt,
    object Payload,
    byte[]? TriggeringEventEnvelope = null,
    string? TriggeringEventTopic = null);
