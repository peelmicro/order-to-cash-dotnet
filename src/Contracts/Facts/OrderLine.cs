namespace OrderToCash.Contracts.Facts;

/// <summary>One line of an order (specs/shared/asyncapi.yaml `components.schemas.OrderLine`).</summary>
public sealed record OrderLine(
    string ProductCode,
    string? Description,
    int Quantity,
    long UnitPrice,
    long LineDiscount);
