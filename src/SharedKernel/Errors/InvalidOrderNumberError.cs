namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when an <see cref="OrderNumber"/> is constructed from a sequence
/// that is not strictly positive, or parsed from a string that does not
/// match the `ORD-######` business-reference shape (domain-model.md §2.3).
/// </summary>
public sealed class InvalidOrderNumberError : DomainError
{
    public InvalidOrderNumberError(string offendingValue)
        : base(
            "order_number.invalid",
            $"'{offendingValue}' is not a valid order number: it must be 'ORD-' followed by a " +
            "zero-padded, strictly positive sequence of at least six digits.")
    {
    }
}
