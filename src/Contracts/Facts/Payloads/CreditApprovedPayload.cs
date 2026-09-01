namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `credit.approved.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.CreditApprovedPayload`).
/// </summary>
public sealed record CreditApprovedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    string Currency,
    long HeldAmount,
    long AvailableCreditAfter);
