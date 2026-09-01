namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.confirmed.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderConfirmedPayload`).
/// </summary>
public sealed record OrderConfirmedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long TotalAmount,
    DateTimeOffset ConfirmedAt);
