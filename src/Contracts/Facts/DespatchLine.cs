namespace OrderToCash.Contracts.Facts;

/// <summary>One despatched line (specs/shared/asyncapi.yaml `components.schemas.DespatchLine`).</summary>
public sealed record DespatchLine(
    string ProductCode,
    int Units);
