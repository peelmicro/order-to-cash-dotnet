using Microsoft.Extensions.Hosting;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// Backlog id 74 bullet 6 (advisory A16) — the guards for the structural
/// consumer-group clearance. No containers: every claim here is about the
/// SHAPE of the teardown, and an unreachable broker address plus a zero
/// budget reproduces the "the group never cleared" path deterministically,
/// which is the path that used to throw from a <c>finally</c> block.
/// </summary>
public sealed class KafkaGroupTeardownGuardTests
{
    /// <summary>
    /// Deliberately NOT <c>"notifications"</c>: recording a leak against the
    /// real group id would make the next real host start wait for it to
    /// clear.
    /// </summary>
    private const string ProbeGroupId = "otc-teardown-guard-probe";

    private const string UnreachableBroker = "127.0.0.1:1";

    /// <summary>
    /// The STRUCTURAL claim. <c>StartHostAsync</c> must hand out a
    /// <see cref="KafkaGroupTestHost"/>, not a bare <see cref="IHost"/> —
    /// that is what makes a future bare <c>host.StopAsync()</c> clear the
    /// group instead of silently reintroducing the shared-group race.
    /// Declared, not merely actual: the static return type is what the
    /// compiler enforces at all every call site.
    /// </summary>
    [Fact]
    public void TheHostHelper_DeclaresAGroupClearingWrapperAsItsReturnType_NotABareIHost()
    {
        var method = typeof(ProjectorTestHost)
            .GetMethod(nameof(ProjectorTestHost.StartAsync))!;

        // Task<THost>
        var hostType = method.ReturnType.GetGenericArguments()[0];

        Assert.True(
            hostType == typeof(KafkaGroupTestHost),
            $"ProjectorTestHost.StartAsync declares its host as '{hostType}'. It must declare "
            + $"'{typeof(KafkaGroupTestHost)}', because that type is what routes a bare host.StopAsync()/Dispose() through the "
            + "consumer-group clearance. Declared as a bare IHost, a future teardown that does not call "
            + "StopHostAndWaitForGroupToClearAsync silently reintroduces the shared-consumer-group race (advisory A16).");
    }

    /// <summary>
    /// The BEHAVIOURAL half of the same claim: a bare <c>StopAsync()</c> —
    /// the exact escape A16 names — runs the clearance, once.
    /// </summary>
    [Fact]
    public async Task ABareStopAsync_OnAHostTheHelperHandsOut_StillRunsTheGroupClearance()
    {
        KafkaGroupClearance.ForgetLeak(ProbeGroupId);
        // TimeSpan.Zero: this case is about WHETHER the clearance runs on a
        // bare StopAsync(), not about how long it waits — the real 150 s budget
        // would spend it all against an unreachable broker and prove the same
        // thing.
        var host = new KafkaGroupTestHost(Host.CreateApplicationBuilder().Build(), UnreachableBroker, ProbeGroupId, TimeSpan.Zero);

        Assert.Equal(0, host.GroupClearancesPerformed);

        await host.StopAsync();

        Assert.True(
            host.GroupClearancesPerformed == 1,
            $"a bare host.StopAsync() ran the consumer-group clearance {host.GroupClearancesPerformed} time(s); it must run it "
            + "exactly once. That is the whole point of the wrapper — the clearance belongs where the host is torn down, not in "
            + "51 per-site finally blocks a future teardown can forget.");

        // And it stays idempotent, so the 51 existing per-site helper calls
        // that follow a StopAsync cost nothing and change nothing.
        await host.StopAsync();
        await host.DisposeAsync();
        Assert.Equal(1, host.GroupClearancesPerformed);

        KafkaGroupClearance.ForgetLeak(ProbeGroupId);
    }

    /// <summary>
    /// Backlog id 74 bullet 6's second half — a test that fails AND whose
    /// teardown also fails must still report ITS OWN failure. Both are made
    /// to happen here in one test: the body raises a named assertion failure,
    /// and the teardown in the <c>finally</c> block is driven against an
    /// unreachable broker with a zero budget, so the group never clears.
    /// </summary>
    [Fact]
    public async Task ATestThatFailsAndWhoseTeardownAlsoFails_StillReportsItsOwnFailure()
    {
        const string originalMessage = "ORIGINAL ASSERTION FAILURE — the message this test must still report";
        KafkaGroupClearance.ForgetLeak(ProbeGroupId);

        var surfaced = await Record.ExceptionAsync(async () =>
        {
            try
            {
                Assert.Fail(originalMessage);
            }
            finally
            {
                await KafkaGroupClearance.StopAndClearAsync(
                    Host.CreateApplicationBuilder().Build(), UnreachableBroker, ProbeGroupId, TimeSpan.Zero);
            }
        });

        // The control: the teardown really did fail. Without it this case would
        // pass vacuously on a teardown that simply succeeded.
        var leak = KafkaGroupClearance.RecordedLeakFor(ProbeGroupId);
        Assert.True(
            leak is not null,
            "no leak was recorded for the probe group after a zero-budget clearance against an unreachable broker, so the teardown "
            + "either succeeded (this case would then prove nothing) or THREW instead of recording — and throwing from a finally "
            + "block is exactly the masking defect this case exists to catch. The exception that escaped was: "
            + $"{surfaced?.GetType().Name ?? "<none>"}: {surfaced?.Message ?? "<none>"}");

        Assert.NotNull(surfaced);
        Assert.True(
            surfaced!.Message.Contains(originalMessage, StringComparison.Ordinal),
            $"the exception that escaped was a {surfaced.GetType().Name} reading \"{surfaced.Message}\". It must be the test's OWN "
            + $"assertion failure (\"{originalMessage}\"). A throw from a finally block REPLACES the exception already in flight, so a "
            + "teardown that throws destroys the failing test's own message — which is exactly what the pre-fix "
            + "StopHostAndWaitForGroupToClearAsync did on every timeout.");

        KafkaGroupClearance.ForgetLeak(ProbeGroupId);
    }
}
