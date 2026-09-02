namespace OrderToCash.Orders.Domain;

/// <summary>
/// The two compensating actions that can precede a cancellation
/// (specs/shared/asyncapi.yaml <c>components.schemas.CompensationStep</c>'s
/// <c>step</c> enum).
/// </summary>
public enum CompensationStepKind
{
    StockReleased,
    CreditReleased,
}

/// <summary>Maps <see cref="CompensationStepKind"/> to its snake_case wire token.</summary>
public static class CompensationStepKinds
{
    public static string ToToken(CompensationStepKind kind) => kind switch
    {
        CompensationStepKind.StockReleased => "stock_released",
        CompensationStepKind.CreditReleased => "credit_released",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognised CompensationStepKind member."),
    };
}
