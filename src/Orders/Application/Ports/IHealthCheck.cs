namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// R60/OR6, design.md §8 — one dependency-reachability probe. The check
/// NAME is one of <c>specs/shared/openapi.yaml</c> <c>HealthResponse.checks</c>'
/// own keys (design.md §8.2's table: <c>writeModel</c>, <c>factStream</c>,
/// <c>rpcTransport</c>, <c>readModel</c>), never invented per service.
///
/// Deliberately NOT the framework's own
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c> —
/// <c>Orders.csproj</c>'s new <c>&lt;FrameworkReference
/// Include="Microsoft.AspNetCore.App" /&gt;</c> (design.md §8.1) makes that
/// type resolvable too, and the two share a bare name. Design.md §9.1
/// explains why the framework's built-in checks are refused even though
/// they need no NuGet package (unlike #7's refusal of <c>@nestjs/terminus</c>,
/// which WAS a package — that specific reason does not transfer here): the
/// shape this repository wants is <c>openapi.yaml</c>'s exact
/// <c>HealthResponse</c> body, produced by
/// <see cref="OrderToCash.Orders.Infrastructure.Health.HealthCheckAggregator"/>,
/// which the framework's own health-check middleware does not emit.
/// <c>using Microsoft.Extensions.Diagnostics.HealthChecks;</c> must never
/// appear anywhere this interface is implemented or consumed —
/// <c>HealthProbeTimeoutTests</c>' enumeration (design.md §8.3, ledger L26)
/// matches implementations by THIS interface's fully-qualified name, never
/// the bare <c>IHealthCheck</c> name the two types happen to share.
/// </summary>
public interface IHealthCheck
{
    /// <summary>The <c>openapi.yaml</c> <c>HealthResponse.checks</c> key this probe reports under.</summary>
    string Name { get; }

    /// <summary>
    /// A REAL call against the dependency, bounded by the check's OWN
    /// explicit timeout (design.md §8.3, ledger L26 — "a stalled-but-open
    /// socket is the case that matters") — never cached, never assumed from
    /// connection state alone.
    /// </summary>
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>The outcome of one <see cref="IHealthCheck.CheckAsync"/> call.</summary>
public readonly record struct HealthCheckResult(bool IsUp, string? Detail)
{
    public static HealthCheckResult Up() => new(true, null);

    public static HealthCheckResult Down(string detail) => new(false, detail);
}
