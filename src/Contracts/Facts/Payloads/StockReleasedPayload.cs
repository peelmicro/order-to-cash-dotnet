namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `stock.released.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.StockReleasedPayload`).
/// <c>RetailerCode</c> is not in the schema's `required` list.
/// </summary>
public sealed record StockReleasedPayload(
    string OrderReference,
    string CompanyCode,
    IReadOnlyList<ReservationRef> Released,
    string Reason,
    string? RetailerCode = null);
