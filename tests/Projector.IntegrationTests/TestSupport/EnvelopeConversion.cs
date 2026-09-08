using OrderToCash.Contracts.Envelopes;
using OrderToCash.Projector.Domain;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

public static class EnvelopeConversion
{
    public static FactEnvelope ToDomain<TPayload>(this Envelope<TPayload> envelope) where TPayload : notnull =>
        new(envelope.EventId, envelope.EventType, envelope.CorrelationId, envelope.CausationId, envelope.OccurredAt, envelope.Payload);
}
