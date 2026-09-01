namespace OrderToCash.Contracts.Facts;

/// <summary>
/// One compensating action that ran before a cancellation
/// (specs/shared/asyncapi.yaml `components.schemas.CompensationStep`).
/// <c>EventId</c> and <c>Summary</c> are not in the schema's `required` list.
/// </summary>
public sealed record CompensationStep(
    string Step,
    string EventType,
    DateTimeOffset OccurredAt,
    Guid? EventId = null,
    string? Summary = null);
