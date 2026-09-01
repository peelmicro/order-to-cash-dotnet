namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `credit.released.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.CreditReleasedPayload`).
/// <c>CreditCode</c> is not in the schema's `required` list.
/// </summary>
public sealed record CreditReleasedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long ReleasedAmount,
    long AvailableCreditAfter,
    string Reason,
    string? CreditCode = null);
