namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.placed.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.OrderPlacedPayload`).
/// </summary>
public sealed record OrderPlacedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string BuyerGln,
    string SupplierGln,
    string Currency,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderLine> Lines,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    string? Notes = null);
