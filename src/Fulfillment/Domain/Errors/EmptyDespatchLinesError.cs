using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="DespatchAdvice.Create"/> when the line list would be
/// empty — <b>F6</b>: a despatch advice always has at least one line
/// (`R36`). Changes nothing and creates no aggregate.
/// </summary>
public sealed class EmptyDespatchLinesError(string orderReference)
    : DomainError("EMPTY_DESPATCH_LINES", $"Despatch advice for order '{orderReference}' would have zero lines — refused (F6).")
{
    public string OrderReference { get; } = orderReference;
}
