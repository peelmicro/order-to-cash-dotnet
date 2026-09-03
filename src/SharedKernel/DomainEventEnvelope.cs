using System.Text.RegularExpressions;
using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// The pure guard R11 needs and no other #8 feature owns (design.md §4.7,
/// §1.1 of <c>outbox_and_idempotency</c>'s requirements): every field of an
/// <see cref="IDomainEventEnvelope"/> is present, non-empty and, for
/// <c>eventType</c>, matches R11's own pattern. No package reference, no
/// framework, no store — <c>payload</c> is deliberately not checked here
/// (design.md §4.7's own note): that half belongs to the outbox writer,
/// which is where JSON is a legal concept.
/// </summary>
public static partial class DomainEventEnvelope
{
    /// <summary>Throws <see cref="IncompleteDomainEventEnvelopeError"/> naming the first field that fails; does nothing otherwise.</summary>
    public static void Validate(IDomainEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        EnsureNotEmpty(envelope.EventId, nameof(envelope.EventId));
        EnsureNotEmpty(envelope.AggregateId, nameof(envelope.AggregateId));
        EnsureNotEmpty(envelope.CorrelationId, nameof(envelope.CorrelationId));
        EnsureNotEmpty(envelope.CausationId, nameof(envelope.CausationId));

        if (envelope.OccurredAt == default)
        {
            throw new IncompleteDomainEventEnvelopeError(nameof(envelope.OccurredAt), "must not be the default (unset) instant");
        }

        if (string.IsNullOrEmpty(envelope.EventType))
        {
            throw new IncompleteDomainEventEnvelopeError(nameof(envelope.EventType), "must not be null or empty");
        }

        if (!EventTypePattern().IsMatch(envelope.EventType))
        {
            throw new IncompleteDomainEventEnvelopeError(
                nameof(envelope.EventType),
                $"'{envelope.EventType}' must match the pattern <aggregate>.<fact>.v<n>");
        }
    }

    private static void EnsureNotEmpty(UniqueId id, string fieldName)
    {
        if (id.Value == Guid.Empty)
        {
            throw new IncompleteDomainEventEnvelopeError(fieldName, "must not wrap an empty GUID");
        }
    }

    // R11's own pattern, transcribed from asyncapi.yaml's Envelope.eventType
    // — not paraphrased. Compile-time generated so no allocation happens per
    // event (design.md §4.7).
    [GeneratedRegex(@"^[a-z]+\.[a-z_]+\.v[0-9]+$")]
    private static partial Regex EventTypePattern();
}
