namespace OrderToCash.Gateway.Presentation;

/// <summary>A single field's validation refusal — openapi.yaml <c>ValidationProblem.errors[]</c>.</summary>
public sealed record ValidationFieldError(string Field, string Message);

/// <summary>Thrown by request parsing this gateway does itself (a malformed route id, an out-of-range page/pageSize) — mapped to <c>400 VALIDATION_FAILED</c> with the openapi <c>ValidationProblem</c> shape by <c>Presentation/Problem/ProblemJsonMiddleware.cs</c>.</summary>
public sealed class GatewayRequestValidationError(IReadOnlyList<ValidationFieldError> errors)
    : Exception(string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
{
    public GatewayRequestValidationError(string field, string message)
        : this([new ValidationFieldError(field, message)])
    {
    }

    public IReadOnlyList<ValidationFieldError> Errors { get; } = errors;
}
