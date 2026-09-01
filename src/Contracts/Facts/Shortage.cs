namespace OrderToCash.Contracts.Facts;

/// <summary>One line that could not be satisfied (specs/shared/asyncapi.yaml `components.schemas.Shortage`).</summary>
public sealed record Shortage(
    string ProductCode,
    int Requested,
    int Available);
