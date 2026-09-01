namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.despatched.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderDespatchedPayload`).
/// </summary>
public sealed record OrderDespatchedPayload(
    string OrderReference,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    string CompanyCode,
    string RetailerCode,
    IReadOnlyList<DespatchLine> Lines);
