namespace OrderToCash.Cqrs;

/// <summary>
/// Thrown by <see cref="DispatcherServiceCollectionExtensions.AddDispatcher"/>
/// when the startup validation pass finds a command or query type with zero
/// registered handlers, or more than one. This is the mechanism behind
/// CLAUDE.md's "startup validation fails fast" rule — thrown during service
/// registration, before the host starts serving traffic, so a missing or
/// duplicated handler is a boot failure rather than a surprise at first use.
/// </summary>
public sealed class DispatcherValidationException : Exception
{
    public DispatcherValidationException(string message)
        : base(message)
    {
    }
}
