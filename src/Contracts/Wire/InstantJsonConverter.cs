using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderToCash.Contracts.Wire;

/// <summary>
/// Serialises an `Instant` (specs/shared/asyncapi.yaml `components.schemas.Instant`)
/// as a UTC ISO-8601 string with exactly three fraction digits and a literal
/// `Z` suffix — e.g. `2026-08-18T10:15:00.000Z` — which is the exact shape of
/// `occurredAt` in every one of the twelve golden envelopes captured from #7
/// (`tests/Contracts.UnitTests/GoldenEnvelopes/*.json`).
/// </summary>
/// <remarks>
/// The BCL's default <see cref="DateTimeOffset"/> round-trip format
/// (`"O"`) would instead write seven fraction digits and a `+00:00` offset
/// rather than `Z`, which would fail the envelope byte-exactness assertion
/// on every golden file. Every value is normalised to UTC on both read and
/// write, so a round trip through this converter always yields the same
/// instant and the same offset (zero), which is what the round-trip test
/// (acceptance item 5) depends on.
/// </remarks>
public sealed class InstantJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string WireFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (raw is null)
        {
            throw new JsonException("Expected a non-null ISO-8601 instant string.");
        }

        return DateTimeOffset.Parse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString(WireFormat, CultureInfo.InvariantCulture));
    }
}
