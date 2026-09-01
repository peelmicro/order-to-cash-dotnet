namespace OrderToCash.Contracts.Facts;

/// <summary>One stock reservation (specs/shared/asyncapi.yaml `components.schemas.ReservationRef`).</summary>
public sealed record ReservationRef(
    Guid ReservationId,
    string ProductCode,
    int Units);
