namespace OrderToCash.Contracts.Facts;

/// <summary>One invoiced line (specs/shared/asyncapi.yaml `components.schemas.InvoiceLine`).</summary>
public sealed record InvoiceLine(
    string ProductCode,
    int Units,
    long UnitPrice);
