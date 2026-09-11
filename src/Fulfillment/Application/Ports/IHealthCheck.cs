namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// R60/OR6, design.md §8 (feature <c>observability_reliability</c>) — one
/// dependency-reachability probe. The check NAME is one of
/// <c>specs/shared/openapi.yaml</c> <c>HealthResponse.checks</c>' own keys
/// (design.md §8.2's table: Fulfillment checks <c>writeModel</c> and
/// <c>rpcTransport</c> only — no Kafka check, Fulfillment consumes no fact
/// and publishes only through its outbox).
///
/// Deliberately NOT the framework's own
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c> — see
/// <c>OrderToCash.Orders.Application.Ports.IHealthCheck</c>'s own remarks,
/// which this copy follows. Never <c>using
/// Microsoft.Extensions.Diagnostics.HealthChecks;</c> anywhere this
/// interface is implemented or consumed.
/// </summary>
public interface IHealthCheck
{
    /// <summary>The <c>openapi.yaml</c> <c>HealthResponse.checks</c> key this probe reports under.</summary>
    string Name { get; }

    /// <summary>A REAL call against the dependency, bounded by the check's OWN explicit timeout (design.md §8.3, ledger L26) — never cached, never assumed.</summary>
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>The outcome of one <see cref="IHealthCheck.CheckAsync"/> call.</summary>
public readonly record struct HealthCheckResult(bool IsUp, string? Detail)
{
    public static HealthCheckResult Up() => new(true, null);

    public static HealthCheckResult Down(string detail) => new(false, detail);
}
