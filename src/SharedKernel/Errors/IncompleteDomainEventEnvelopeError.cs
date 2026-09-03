namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised by <see cref="DomainEventEnvelope.Validate"/> when an
/// <see cref="IDomainEventEnvelope"/> is missing one of the six envelope
/// fields R11 requires — "no field absent, null or empty" — or carries an
/// <c>eventType</c> that does not match <c>&lt;aggregate&gt;.&lt;fact&gt;.v&lt;n&gt;</c>.
/// The message names the specific field that failed, so a caller does not
/// have to re-derive which of the six checks fired.
/// </summary>
public sealed class IncompleteDomainEventEnvelopeError : DomainError
{
    public IncompleteDomainEventEnvelopeError(string fieldName, string reason)
        : base("domain_event_envelope.incomplete", $"Domain event envelope field '{fieldName}' {reason}.")
    {
    }
}
