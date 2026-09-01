using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderToCash.Contracts.Wire;

/// <summary>
/// The ONE <see cref="JsonSerializerOptions"/> every service uses to read or
/// write the wire — CLAUDE.md's non-negotiable: "camelCase, nulls omitted, no
/// `$type` discriminator, no PascalCase envelope, set once in a shared
/// `JsonSerializerOptions` in `Contracts` so no service can drift". There is
/// deliberately no second way to obtain a compatible options instance:
/// <see cref="Options"/> is the only public member, it is not mutable at the
/// call site (mutate it and every consumer sees the mutation — the settings
/// below are exactly what should be shared), and nothing in this class
/// registers a polymorphic type discriminator, which is what keeps
/// `$type` out of the wire.
/// </summary>
public static class JsonWire
{
    /// <summary>
    /// camelCase property names, nulls omitted from the output
    /// (<see cref="JsonIgnoreCondition.WhenWritingNull"/> — an empty
    /// collection or empty string is NOT omitted, only an actual
    /// <see langword="null"/> reference, which is the behaviour the golden
    /// envelopes show: e.g. `order.placed.v1`'s optional `notes` field is
    /// absent only when it was never set, not merely empty), no indentation
    /// (matches the compact single-line shape of every captured golden
    /// file), and unescaped Unicode so a business name round-trips exactly
    /// (`é`, `—`, …) instead of turning into `\uXXXX` escapes.
    /// </summary>
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new InstantJsonConverter());

        return options;
    }
}
