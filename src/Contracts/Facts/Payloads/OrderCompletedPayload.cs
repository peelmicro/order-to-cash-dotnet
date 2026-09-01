namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.completed.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderCompletedPayload`).
/// </summary>
public sealed record OrderCompletedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long TotalAmount,
    DateTimeOffset CompletedAt);
