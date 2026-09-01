namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `stock.rejected.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.StockRejectedPayload`).
/// <c>RetailerCode</c> is not in the schema's `required` list. No golden
/// envelope exists for this fact — see progress/impl_contracts_package.md.
/// </summary>
public sealed record StockRejectedPayload(
    string OrderReference,
    string CompanyCode,
    IReadOnlyList<Shortage> Shortages,
    string Reason,
    string? RetailerCode = null);
