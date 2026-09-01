namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `payment.received.v1`
/// (specs/shared/asyncapi.yaml `components.schemas.PaymentReceivedPayload`).
/// </summary>
public sealed record PaymentReceivedPayload(
    string OrderReference,
    string InvoiceReference,
    string PaymentReference,
    string Currency,
    long Amount,
    DateTimeOffset ValueDate,
    string Source);
