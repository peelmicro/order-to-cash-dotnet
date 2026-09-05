// COPY OF — src/Orders/Infrastructure/Outbox/OutboxEnvelopeMapper.cs
using System.Text.Json;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// row -&gt; <see cref="Envelope{TPayload}"/> -&gt; wire bytes, via
/// <see cref="JsonWire.Options"/> and nothing else. The relay's ONLY source
/// for every field: no clock, no <c>Guid.NewGuid()</c>, no default.
/// </summary>
public static class OutboxEnvelopeMapper
{
    /// <summary>
    /// <see cref="JsonElement"/> rather than a typed payload, deliberately:
    /// serialising a <see cref="JsonElement"/> writes the stored text
    /// through unchanged, so what a consumer receives is byte-for-byte what
    /// the producing transaction committed. The <see cref="JsonDocument"/>
    /// is disposed within this method, after the bytes are produced
    /// (<c>CA2213</c> is an error in this repository).
    /// </summary>
    public static ReadOnlyMemory<byte> ToWireBytes(OutboxMessage row)
    {
        using var document = JsonDocument.Parse(row.Payload);

        var envelope = new Envelope<JsonElement>(
            EventId: row.EventId,
            EventType: row.EventType,
            AggregateId: row.AggregateId,
            CorrelationId: row.CorrelationId,
            CausationId: row.CausationId,
            OccurredAt: new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
            Payload: document.RootElement);

        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
    }
}
