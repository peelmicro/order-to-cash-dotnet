namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `credit.rejected.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.CreditRejectedPayload`).
/// <c>CreditCode</c> is not in the schema's `required` list.
/// </summary>
public sealed record CreditRejectedPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long RequestedAmount,
    long AvailableCredit,
    string Reason,
    string? CreditCode = null);
