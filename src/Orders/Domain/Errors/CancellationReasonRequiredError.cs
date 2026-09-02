using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when <c>CancellationReasons.Parse</c> is given <see langword="null"/>,
/// an empty string or whitespace — invariant O6
/// (specs/shared/requirements.md R10). A missing reason is a contract
/// failure by the sender, distinct from
/// <see cref="UnknownCancellationReasonError"/>'s vocabulary failure
/// (design.md §6.2).
/// </summary>
public sealed class CancellationReasonRequiredError : DomainError
{
    public CancellationReasonRequiredError()
        : base("order.cancellation_reason_required", "A cancellation reason is required and none was supplied.")
    {
    }
}
