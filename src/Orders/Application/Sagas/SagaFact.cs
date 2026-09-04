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
public sealed record SagaFact(
    Guid EventId,
    string EventType,
    Guid AggregateId,
    Guid CorrelationId,
    Guid CausationId,
    DateTimeOffset OccurredAt,
    object Payload);
