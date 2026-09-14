using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>
/// Backlog id 74 bullet 6 (advisory A16) — the consumer-group clearance
/// feature 27 added, moved OUT of 51 per-site <c>finally</c> blocks and into
/// the one place a host is built and disposed.
///
/// <para>
/// Two things changed, and the second is the reason this type exists at all.
/// </para>
///
/// <para>
/// <b>Structural placement.</b> The clearance now lives inside
/// <see cref="KafkaGroupTestHost"/>, which is what
/// <c>ProjectorTestHost.StartAsync</c> returns. A future
/// bare <c>host.StopAsync()</c> on such a host therefore CLEARS THE GROUP
/// rather than silently reintroducing the shared-consumer-group race — the
/// escape A16 named. The per-site helper call is kept and is idempotent, so no
/// existing test needed to change.
/// </para>
///
/// <para>
/// <b>A teardown may not mask its test's own failure.</b> The previous shape
/// threw a <see cref="TimeoutException"/> from a <c>finally</c> block. In C# an
/// exception thrown from <c>finally</c> REPLACES the one already in flight, so
/// a test that failed its own assertion AND whose teardown timed out reported
/// the teardown's timeout and lost its own message entirely. The clearance
/// therefore records a leak instead of throwing, and the enforcement moves to
/// <see cref="EnsureGroupIsClearBeforeStartAsync"/> — the SETUP path of the
/// next host, which is never inside a <c>finally</c> and so cannot mask
/// anything.
/// </para>
/// </summary>
public static class KafkaGroupClearance
{
    private static readonly Lock _leakGate = new();
    private static readonly Dictionary<string, string> _leaks = new(StringComparer.Ordinal);

    /// <summary>The default budget feature 27 established, unchanged.</summary>
    public static TimeSpan DefaultBudget => TimeSpan.FromSeconds(150);

    /// <summary>
    /// Stops <paramref name="host"/> and waits for <paramref name="groupId"/>
    /// to report no members. NEVER THROWS: a failure to stop, a failure to
    /// dispose and a failure to clear are all recorded rather than raised, so
    /// that calling this from a <c>finally</c> block cannot replace the
    /// exception the test itself is reporting.
    /// </summary>
    public static async Task StopAndClearAsync(IHost host, string bootstrapServers, string groupId, TimeSpan? timeout)
    {
        try
        {
            await host.StopAsync();
        }
        catch (Exception ex)
        {
            RecordLeak(groupId, $"host.StopAsync() for group '{groupId}' threw {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            host.Dispose();
        }
        catch (Exception ex)
        {
            RecordLeak(groupId, $"host.Dispose() for group '{groupId}' threw {ex.GetType().Name}: {ex.Message}");
        }

        var budget = timeout ?? DefaultBudget;
        var startedAt = DateTime.UtcNow;

        if (!await WaitForGroupToClearAsync(bootstrapServers, groupId, budget))
        {
            RecordLeak(
                groupId,
                $"Consumer group '{groupId}' still reported members {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s after a test's own host was stopped — "
                + "that teardown left a stale member which blocks the NEXT test's rebalance (observed directly: a silent member can hold every partition of "
                + "a topic for well over 90s, `zombieprobe` reproduction).");
        }
    }

    /// <summary>Polls the broker until <paramref name="groupId"/> reports no members, or <paramref name="budget"/> expires. Returns whether it cleared.</summary>
    public static async Task<bool> WaitForGroupToClearAsync(string bootstrapServers, string groupId, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;

        // Constructed only if there is budget left to use it. Building — and
        // above all DISPOSING — a librdkafka admin client pointed at an
        // unreachable broker is not free, and a zero budget means the caller
        // has already decided not to wait.
        if (DateTime.UtcNow >= deadline)
        {
            return false;
        }

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await admin.DescribeConsumerGroupsAsync([groupId], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                var description = result.ConsumerGroupDescriptions.SingleOrDefault(g => g.GroupId == groupId);
                if (description is null || description.Members.Count == 0)
                {
                    return true;
                }
            }
            catch (KafkaException)
            {
                // DescribeConsumerGroupsAsync itself can transiently fail
                // under the SAME contention that motivates this wait — retry
                // within the budget rather than surface a spurious failure
                // from the PROBE itself.
            }

            await Task.Delay(300);
        }

        return false;
    }

    /// <summary>
    /// The enforcement half, on the SETUP path. A no-op unless a previous
    /// teardown recorded a leak for this group; if one did, this waits for the
    /// group to clear and throws — naming the recorded leak — only if it never
    /// does. Never inside a <c>finally</c>, so it cannot replace a test's own
    /// failure.
    /// </summary>
    public static async Task EnsureGroupIsClearBeforeStartAsync(string bootstrapServers, string groupId)
    {
        var leak = RecordedLeakFor(groupId);
        if (leak is null)
        {
            return;
        }

        if (await WaitForGroupToClearAsync(bootstrapServers, groupId, DefaultBudget))
        {
            ForgetLeak(groupId);
            return;
        }

        throw new TimeoutException(
            $"A previous test's teardown left consumer group '{groupId}' with members, and it has still not cleared. "
            + $"The recorded leak was: {leak}");
    }

    public static void RecordLeak(string groupId, string message)
    {
        lock (_leakGate)
        {
            _leaks[groupId] = message;
        }

        Console.Error.WriteLine($"[KafkaGroupClearance] {message}");
    }

    public static string? RecordedLeakFor(string groupId)
    {
        lock (_leakGate)
        {
            return _leaks.TryGetValue(groupId, out var message) ? message : null;
        }
    }

    public static void ForgetLeak(string groupId)
    {
        lock (_leakGate)
        {
            _leaks.Remove(groupId);
        }
    }
}
