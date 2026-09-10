namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.cancelled.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderCancelledPayload`).
/// <c>Note</c> (SA-2) is optional — trailing, defaulted, matching every
/// other optional payload member's own declaration in this directory
/// (e.g. <see cref="OrderPlacedPayload.Notes"/>): absent unless an operator
/// supplied one, and always absent for a saga-decided cancellation
/// (<c>stock_rejected</c>/<c>credit_rejected</c>), which never carries a
/// note in the first place.
/// </summary>
public sealed record OrderCancelledPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CancellationReason,
    DateTimeOffset CancelledAt,
    IReadOnlyList<CompensationStep> CompensationSteps,
    string? Note = null);
