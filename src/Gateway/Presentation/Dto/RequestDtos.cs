namespace OrderToCash.Gateway.Presentation.Dto;

// Request bodies bound directly from JSON via ASP.NET Core's Minimal API
// model binding, using the app-wide JsonWire options (camelCase,
// case-insensitive) configured in GatewayHost — one C# shape per openapi.yaml
// request schema.

public sealed record LoginRequestDto(string Username, string Password);

public sealed record PlaceOrderRequestLineDto(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

public sealed record PlaceOrderRequestDto(
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<PlaceOrderRequestLineDto>? Lines,
    long? OrderDiscount,
    string? Notes);

public sealed record CancelOrderRequestDto(string? Note);

public sealed record ReplenishStockRequestLineDto(string ProductCode, int Units);

public sealed record ReplenishStockRequestDto(string CompanyCode, IReadOnlyList<ReplenishStockRequestLineDto>? Lines);

public sealed record MoneyDto(long Amount, string Currency);

public sealed record RegisterPaymentRequestDto(string PaymentReference, MoneyDto Amount, DateTimeOffset ValueDate, string Source);
