using OrderToCash.Gateway.Application.Ports;

namespace OrderToCash.Gateway.Infrastructure.Health;

public sealed record HealthResponseDto(string Status, IReadOnlyDictionary<string, CheckResultDto>? Checks);

public sealed record CheckResultDto(string Status, string? Detail);

/// <summary>design.md §8.2 — pure aggregation over the registered <see cref="IHealthCheck"/> set, separated from the endpoint-mapping code (<c>HealthEndpoints</c>) so <c>HealthCheckAggregationTests</c> drives it directly, over fakes, with no HTTP involved.</summary>
public static class HealthCheckAggregator
{
    /// <summary><c>GET /health/live</c> — consults NOTHING, always <c>200 up</c>.</summary>
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
