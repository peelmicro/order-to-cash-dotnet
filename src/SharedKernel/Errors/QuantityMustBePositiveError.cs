namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when a <see cref="Quantity"/> is constructed from a value that is
/// not a strictly positive integer — zero, negative or fractional
/// (specs/shared/requirements.md R3).
/// </summary>
public sealed class QuantityMustBePositiveError : DomainError
{
    public QuantityMustBePositiveError(string offendingValue)
        : base(
            "quantity.must_be_strictly_positive_integer",
            $"A quantity must be a strictly positive integer; received '{offendingValue}'.")
    {
    }
}
