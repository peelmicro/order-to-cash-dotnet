namespace OrderToCash.Gateway.Application.Ports;

/// <summary>
/// R60/OR6, design.md §8 — one dependency-reachability probe. The Gateway
/// checks <c>rpcTransport</c> (NATS) and <c>readModel</c> (MongoDB) — it
/// owns no write model. Unlike the five other services, the Gateway maps
/// <c>GET /health/live</c>/<c>GET /health/ready</c> into its EXISTING
/// <c>WebApplication</c> pipeline (design.md §8.1) rather than through a
/// separate <c>HealthProbeService</c>/port.
///
/// Deliberately NOT the framework's own
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c> — see
/// <c>OrderToCash.Orders.Application.Ports.IHealthCheck</c>'s own remarks.
/// </summary>
public interface IHealthCheck
{
    string Name { get; }

    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}

public readonly record struct HealthCheckResult(bool IsUp, string? Detail)
{
    public static HealthCheckResult Up() => new(true, null);

    public static HealthCheckResult Down(string detail) => new(false, detail);
}
