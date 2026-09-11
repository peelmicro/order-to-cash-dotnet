using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Health;

/// <summary><c>specs/shared/openapi.yaml</c>'s <c>HealthResponse</c> schema — <c>{status, checks?}</c>, checks keyed by name, nulls omitted (Contracts.Wire.JsonWire, the same wire options as every other fact/RPC payload).</summary>
public sealed record HealthResponseDto(string Status, IReadOnlyDictionary<string, CheckResultDto>? Checks);

/// <summary>One entry of <see cref="HealthResponseDto.Checks"/> — <c>{status, detail?}</c>.</summary>
public sealed record CheckResultDto(string Status, string? Detail);

/// <summary>
/// design.md §8.2 — pure aggregation logic over the registered
/// <see cref="IHealthCheck"/> set, deliberately separated from
/// <see cref="HealthProbeService"/>'s Kestrel/routing plumbing so
/// <c>HealthCheckAggregationTests</c> (tasks.md A4f) drives it directly,
/// over fakes, with no HTTP involved at all.
/// </summary>
public static class HealthCheckAggregator
{
    /// <summary><c>GET /health/live</c> — "deliberately independent of dependencies" (openapi.yaml) — consults NOTHING, always <c>200 up</c>.</summary>
    public static HealthResponseDto Live() => new("up", null);

    /// <summary><c>GET /health/ready</c> — runs every check, <c>200</c> when all are up, <c>503</c> naming the failing one(s) otherwise.</summary>
    public static async Task<(int StatusCode, HealthResponseDto Body)> ReadyAsync(IReadOnlyList<IHealthCheck> checks, CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, CheckResultDto>(StringComparer.Ordinal);
        var allUp = true;

        foreach (var check in checks)
        {
            var result = await check.CheckAsync(cancellationToken).ConfigureAwait(false);
            results[check.Name] = new CheckResultDto(result.IsUp ? "up" : "down", result.Detail);
            if (!result.IsUp)
            {
                allUp = false;
            }
        }

        var body = new HealthResponseDto(allUp ? "up" : "down", results);
        return (allUp ? 200 : 503, body);
    }
}
