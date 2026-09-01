namespace OrderToCash.SharedKernel;

/// <summary>
/// Base type for every domain error — "a refusal raised inside the domain
/// layer carrying a stable code; it changes no state and emits no fact"
/// (specs/shared/requirements.md, vocabulary). Every concrete domain error in
/// this repository extends this type (CLAUDE.md, coding conventions:
/// "Domain errors extend DomainError and carry a stable Code").
/// </summary>
public abstract class DomainError : Exception
{
    protected DomainError(string code, string message)
        : base(message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A domain error code must not be null, empty or whitespace.", nameof(code));
        }

        Code = code;
    }

    /// <summary>
    /// A stable, machine-matchable identifier for the refusal — stable across
    /// releases so callers (including across a service boundary) can branch
    /// on <see cref="Code"/> rather than parsing <see cref="Exception.Message"/>.
    /// </summary>
    public string Code { get; }
}
