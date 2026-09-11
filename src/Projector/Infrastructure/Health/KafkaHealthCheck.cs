using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Health;

/// <summary>
/// design.md §8.2's <c>factStream</c> check, ledger L27 — a DEDICATED,
/// short-timeout <see cref="IAdminClient"/> metadata request, never this
/// service's own long-lived producer or consumer, whose retry/backoff
/// policy would stretch one "down" observation over many seconds. A fresh
/// <see cref="IAdminClient"/> per PROCESS (not per call — building one per
/// check would itself take longer than the probe), disposed with the
/// container.
/// </summary>
public sealed class KafkaHealthCheck : IHealthCheck, IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);
    private readonly IAdminClient _admin;

    public KafkaHealthCheck(IOptions<HealthOptions> options)
    {
        _admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = options.Value.KafkaBootstrapServers,
            SocketTimeoutMs = (int)_timeout.TotalMilliseconds,
        }).Build();
    }

    public string Name => "factStream";

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        // A4e addendum — production defect: GetMetadata is SYNCHRONOUS and,
        // called directly here, blocks whatever ThreadPool thread Kestrel
        // dispatched this request on for up to _timeout. Under concurrent
        // load (many /health/ready polls arriving while the broker is
        // down) that ties up worker threads the SAME pool also needs to
        // dispatch /health/live, and measured under an artificially
        // constrained pool this starved liveness for seconds (up to 4s,
        // against a near-instant baseline). TaskCreationOptions.LongRunning
        // runs the blocking call on a DEDICATED thread outside the pool,
        // so it never competes with request dispatch for a pool slot.
        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    // GetMetadata's own TimeSpan argument is the bound — a
                    // paused broker's stalled-but-open socket never returns
                    // a reply, and this call throws KafkaException on that
                    // bound rather than hanging (design.md §8.3).
                    _admin.GetMetadata(_timeout);
                    return HealthCheckResult.Up();
                }
                catch (KafkaException ex)
                {
                    return HealthCheckResult.Down(ex.Message);
                }
            },
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Dispose() => _admin.Dispose();
}
