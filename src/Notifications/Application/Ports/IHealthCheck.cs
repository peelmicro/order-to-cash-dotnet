namespace OrderToCash.Notifications.Application.Ports;

/// <summary>
/// R60/OR6, design.md §8 — one dependency-reachability probe. Notifications
/// checks <c>writeModel</c> and <c>factStream</c> only — it owns no RPC
/// client and no read model.
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
