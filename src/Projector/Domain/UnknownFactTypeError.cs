using OrderToCash.SharedKernel;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// Raised by <see cref="FactProjection.Project(FactEnvelope)"/> when the
/// envelope's payload's CLR type has no arm in the switch — should be
/// unreachable in production because <c>FactCatalog</c>/<c>PR2</c> keep the
/// switch and the catalogue in agreement, but the domain stays total rather
/// than silently ignoring an unrecognised shape.
/// </summary>
public sealed class UnknownFactTypeError : DomainError
{
    public UnknownFactTypeError(string eventType)
        : base("PROJECTOR.UNKNOWN_FACT_TYPE", $"No projection arm exists for eventType '{eventType}'.")
    {
        EventType = eventType;
    }

    public string EventType { get; }
}
