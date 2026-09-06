using OrderToCash.Fulfillment.Domain.Events;

namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// The seam the outbox writer dispatches through to turn a domain event into
/// its OrderToCash.Contracts.Facts.Payloads record. One implementation per
/// service, in Infrastructure/Outbox/, because the mapping is this service's
/// own; the writer that calls it is byte-identical in all three (design.md
/// §8.2.3). Pure: no I/O, no clock, no DI graph of its own.
/// </summary>
public interface IFactPayloadMapper
{
    /// <summary>Throws, naming the CLR type and the eventType, for an event this service does not map.</summary>
    object ToPayload(FactEvent factEvent);
}
