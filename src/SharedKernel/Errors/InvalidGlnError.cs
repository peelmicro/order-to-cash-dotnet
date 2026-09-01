namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when a <see cref="GLN"/> is constructed from a value that is not
/// exactly 13 decimal digits, or whose final digit is not the correct GS1
/// mod-10 check digit over the preceding twelve (specs/shared/requirements.md
/// R4; domain-model.md §2.4).
/// </summary>
public sealed class InvalidGlnError : DomainError
{
    public InvalidGlnError(string offendingValue)
        : base(
            "gln.invalid",
            $"'{offendingValue}' is not a valid GLN: it must be exactly 13 decimal digits whose final " +
            "digit is the correct GS1 mod-10 check digit over the preceding twelve.")
    {
    }
}
