using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Health;

public sealed record HealthResponseDto(string Status, IReadOnlyDictionary<string, CheckResultDto>? Checks);

public sealed record CheckResultDto(string Status, string? Detail);

/// <summary>design.md §8.2 — pure aggregation over the registered <see cref="IHealthCheck"/> set.</summary>
public static class HealthCheckAggregator
{
    public static HealthResponseDto Live() => new("up", null);

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
