using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// One compensating action that ran before a cancellation, carried on
/// <c>order.cancelled.v1</c> only — never stored on the aggregate, never
/// persisted (design.md §6.1). Mirrors
/// specs/shared/asyncapi.yaml <c>components.schemas.CompensationStep</c>.
/// </summary>
public sealed record OrderCompensationStep(
    CompensationStepKind Step,
    UniqueId? EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? Summary);
