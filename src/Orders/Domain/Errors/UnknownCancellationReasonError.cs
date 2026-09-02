using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when <c>CancellationReasons.Parse</c> is given a non-empty token
/// outside the closed set of three — invariant O6
/// (specs/shared/requirements.md R10). Usually a version skew rather than a
/// missing value, which is why it is a distinct code from
/// <see cref="CancellationReasonRequiredError"/> (design.md §6.2).
/// </summary>
public sealed class UnknownCancellationReasonError : DomainError
{
    public UnknownCancellationReasonError(string offendingToken)
        : base("order.cancellation_reason_unknown", $"'{offendingToken}' is not a recognised cancellation reason.")
    {
        OffendingToken = offendingToken;
    }

    public string OffendingToken { get; }
}
