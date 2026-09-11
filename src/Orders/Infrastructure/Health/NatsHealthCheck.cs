using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Health;

/// <summary>
/// design.md §8.2's <c>rpcTransport</c> check — <see cref="INatsClient.PingAsync"/>
/// (round-trip PING/PONG) on the SAME singleton <see cref="INatsConnection"/>
/// every other NATS caller in this service shares (multiplexed, safe to
/// share), bounded by an explicit <see cref="CancellationTokenSource"/>
/// timeout (design.md §8.3 — "a docker-paused broker leaves the TCP
/// connection open ..." — #7's Gateway NATS probe shipped without one,
/// found and disclosed by #7's own IMPLEMENTER
/// (<c>order-to-cash-nestjs/progress/impl_observability_reliability.md:912</c>,
/// the mechanism at <c>:927</c>) and only CONFIRMED by #7's reviewer
/// (<c>order-to-cash-nestjs/progress/review_observability_reliability.md:70-72</c>);
/// review round 1, R2).
/// </summary>
public sealed class NatsHealthCheck(INatsConnection connection) : IHealthCheck
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    public string Name => "rpcTransport";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            await connection.PingAsync(cts.Token).ConfigureAwait(false);
            return HealthCheckResult.Up();
        }
        catch (Exception ex) when (ex is OperationCanceledException or NatsException)
        {
            return HealthCheckResult.Down(ex.Message);
        }
    }
}
