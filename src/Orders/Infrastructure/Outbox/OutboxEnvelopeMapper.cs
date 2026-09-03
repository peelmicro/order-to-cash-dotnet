using System.Text.Json;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// row -&gt; <see cref="Envelope{TPayload}"/> -&gt; wire bytes, via
/// <see cref="JsonWire.Options"/> and nothing else (design.md §5.5). The
/// relay's ONLY source for every field: no clock, no <c>Guid.NewGuid()</c>,
/// no default — OI1's "no field inferred at publication time".
/// </summary>
public static class OutboxEnvelopeMapper
{
    /// <summary>
    /// <see cref="JsonElement"/> rather than a typed payload, deliberately:
    /// serialising a <see cref="JsonElement"/> writes the stored text
    /// through unchanged, so what a consumer receives is byte-for-byte what
    /// the producing transaction committed — round-tripping through a typed
    /// payload would silently drop any field the C# record does not
    /// declare. The <see cref="JsonDocument"/> is disposed within this
    /// method, after the bytes are produced (<c>CA2213</c> is an error in
    /// this repository).
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
            // `occurred_at` is DateTime (Kind Unspecified, read back from
            // datetime2(3)) in the row and DateTimeOffset on the envelope:
            // never the implicit conversion, which would apply the
            // machine's local offset (design.md §5.5).
            OccurredAt: new DateTimeOffset(row.OccurredAt, TimeSpan.Zero),
            Payload: document.RootElement);

        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
    }
}
