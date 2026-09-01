namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `stock.reserved.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.StockReservedPayload`).
/// <c>RetailerCode</c> is not in the schema's `required` list.
/// </summary>
public sealed record StockReservedPayload(
    string OrderReference,
    string CompanyCode,
    IReadOnlyList<ReservationRef> Reservations,
    string? RetailerCode = null);
