using Microsoft.Extensions.Hosting;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>
/// Backlog id 74 bullet 6 (advisory A16) — the STRUCTURAL home of the
/// consumer-group clearance.
///
/// <para>
/// Feature 27 applied <c>StopHostAndWaitForGroupToClearAsync</c> at 51 call
/// sites across three projects. That is a per-site discipline, and A16's point
/// is that a future bare <c>host.StopAsync()</c> in any of them silently
/// reintroduces the shared-consumer-group race. This wrapper removes that
/// possibility rather than documenting it: every host the test-support helper
/// builds IS one of these, and <see cref="StopAsync"/>,
/// <see cref="Dispose"/> and <see cref="DisposeAsync"/> all route through the
/// SAME idempotent clearance. There is no way to tear such a host down without
/// it.
/// </para>
/// </summary>
public sealed class KafkaGroupTestHost : IHost, IAsyncDisposable
{
    private readonly IHost _inner;
    private readonly string _bootstrapServers;
    private readonly string _groupId;
    private readonly TimeSpan? _budget;
    private readonly Lock _gate = new();
    private bool _tornDown;
    private int _clearances;

    /// <param name="inner">The real service host being wrapped — every <see cref="IHost"/> member delegates to it unchanged.</param>
    /// <param name="bootstrapServers">The Testcontainers broker this host's consumers are bound to, reused for the clearance probe.</param>
    /// <param name="groupId">The consumer group whose members must have left the broker before teardown is allowed to complete.</param>
    /// <param name="budget">The clearance budget a BARE <c>StopAsync()</c>/<c>Dispose()</c> uses — <see langword="null"/> for <see cref="KafkaGroupClearance.DefaultBudget"/>. The guard test passes <see cref="TimeSpan.Zero"/> so it can drive the "never cleared" path without waiting.</param>
    public KafkaGroupTestHost(IHost inner, string bootstrapServers, string groupId, TimeSpan? budget = null)
    {
        _inner = inner;
        _bootstrapServers = bootstrapServers;
        _groupId = groupId;
        _budget = budget;
    }

    public IServiceProvider Services => _inner.Services;

    /// <summary>The consumer group every host built by this project's own test support joins.</summary>
    public string GroupId => _groupId;

    /// <summary>How many times the clearance actually ran — exactly one after any teardown, zero before. The guard test reads this.</summary>
    public int GroupClearancesPerformed => Volatile.Read(ref _clearances);

    public Task StartAsync(CancellationToken cancellationToken = default) => _inner.StartAsync(cancellationToken);

    /// <summary>Deliberately NOT a pass-through — this is the escape A16 names, closed at the type.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => StopAndClearGroupAsync(_budget);

    public void Dispose() => StopAndClearGroupAsync(_budget).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync() => await StopAndClearGroupAsync(_budget);

    /// <summary>Idempotent: the first caller performs the teardown, every later caller returns immediately, so the existing per-site helper calls remain correct and free.</summary>
    public async Task StopAndClearGroupAsync(TimeSpan? timeout)
    {
        lock (_gate)
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
        }

        Interlocked.Increment(ref _clearances);
        await KafkaGroupClearance.StopAndClearAsync(_inner, _bootstrapServers, _groupId, timeout ?? _budget);
    }
}
