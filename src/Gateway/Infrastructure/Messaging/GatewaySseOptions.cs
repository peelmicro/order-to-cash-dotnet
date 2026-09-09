namespace OrderToCash.Gateway.Infrastructure.Messaging;

/// <summary>
/// <c>GET /orders/stream</c>'s two configuration knobs — ported from #7's
/// <c>infrastructure/messaging/sse.config.ts</c> (<c>SseConfig</c>/
/// <c>loadSseConfig</c>), same env var names, same defaults (500 / 15 000).
/// <see cref="FromEnvironment"/> follows this repository's own established
/// <c>*Options.FromEnvironment()</c> shape (<c>JwtOptions</c>,
/// <c>LoginThrottleOptions</c>) — malformed or non-positive raw values fall
/// back to the default rather than throwing at boot.
/// </summary>
public sealed class GatewaySseOptions
{
    public int BufferCapacity { get; set; } = 500;

    public int PingIntervalMs { get; set; } = 15_000;

    public static GatewaySseOptions FromEnvironment()
    {
        return new GatewaySseOptions
        {
            BufferCapacity = ParsePositiveInt(Environment.GetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY"), 500),
            PingIntervalMs = ParsePositiveInt(Environment.GetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS"), 15_000),
        };
    }

    private static int ParsePositiveInt(string? raw, int fallback) =>
        !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
