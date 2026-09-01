namespace OrderToCash.Contracts.Facts.Payloads;

/// <summary>
/// Payload of `order.saga_failed.v1`, the 14th fact
/// (specs/shared/asyncapi.yaml `components.schemas.OrderSagaFailedPayload`).
/// Diagnostic only — no golden envelope exists for it (#7's retained topics
/// held no instance at capture time; see
/// progress/impl_contracts_package.md).
/// </summary>
public sealed record OrderSagaFailedPayload(
    string OrderReference,
    string Command,
    int Attempts,
    string LastError,
    DateTimeOffset FailedAt);
