namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when a <see cref="UniqueId"/> is constructed from an empty GUID —
/// identity is never inferred from a store's auto-increment and is never
/// the absence of a value either (domain-model.md §2.5).
/// </summary>
public sealed class InvalidUniqueIdError : DomainError
{
    public InvalidUniqueIdError()
        : base("unique_id.empty", "A UniqueId must not wrap an empty GUID.")
    {
    }
}
