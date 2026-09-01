namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.cancelled.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderCancelledPayload`).
/// </summary>
public sealed record OrderCancelledPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CancellationReason,
    DateTimeOffset CancelledAt,
    IReadOnlyList<CompensationStep> CompensationSteps);
