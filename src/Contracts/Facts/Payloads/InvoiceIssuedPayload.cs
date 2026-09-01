namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `invoice.issued.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.InvoiceIssuedPayload`).
/// </summary>
public sealed record InvoiceIssuedPayload(
    string OrderReference,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<InvoiceLine> Lines,
    long Amount,
    long Discount,
    long TotalAmount);
