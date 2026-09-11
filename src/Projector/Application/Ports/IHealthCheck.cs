namespace OrderToCash.Projector.Application.Ports;

/// <summary>
/// R60/OR6, design.md §8 — one dependency-reachability probe. Projector
/// checks <c>factStream</c> (Kafka), <c>rpcTransport</c> (NATS — it
/// publishes update signals) and <c>readModel</c> (MongoDB) — it owns no
/// write model.
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
