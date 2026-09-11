using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.Health;

/// <summary>design.md §8.2's <c>writeModel</c> check — a real <c>SELECT 1</c> against a FRESH <see cref="SqlConnection"/> (never cached, never the pooled DbContext), bounded by an explicit timeout (design.md §8.3, ledger L26).</summary>
public sealed class MsSqlHealthCheck(IOptions<HealthOptions> options) : IHealthCheck
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    public string Name => "writeModel";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            // Pooling=false — A4e addendum, production defect: a POOLED
            // physical connection warmed while the server was healthy and
            // then reused after the server went unresponsive can wait on
            // the attention ack well past CancelAfter(_timeout) (observed
            // live: a pooled reuse against a paused container hung for
            // several minutes with no cancellation ever taking effect).
            // A genuinely fresh, unpooled TCP connect DOES honour
            // ConnectTimeout/the linked token, because there is no stuck
            // established session for the cancellation to fail to unstick.
            var connectionStringBuilder = new SqlConnectionStringBuilder(options.Value.ConnectionString)
            {
                ConnectTimeout = (int)_timeout.TotalSeconds,
                Pooling = false,
            };

            await using var connection = new SqlConnection(connectionStringBuilder.ConnectionString);
            await connection.OpenAsync(cts.Token).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = (int)_timeout.TotalSeconds;
            await command.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);

            return HealthCheckResult.Up();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Down(ex.Message);
        }
    }
}
