using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Orders.Infrastructure;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Backlog id 90, bullet 3 — the decision, and its enforcement.
/// <c>OrdersSagaOptions.Dispatch.DegreeOfParallelism</c> arriving below 1
/// through configuration FAILS FAST at composition time; it is never clamped
/// silently. CLAUDE.md's standing rule is that DI and configuration failures
/// must be loud at boot, and this value's quiet failure is the worst shape
/// this codebase has a name for: at 0, the dispatch worker's
/// <c>ExecuteAsync</c> completes immediately, the host stays up and reports
/// healthy, and the saga fast path dispatches nothing for any order, forever.
/// </summary>
/// <remarks>
/// The <c>Math.Max(1, ...)</c> floor inside
/// <c>SagaCommandDispatchWorker.ExecuteAsync</c> is deliberately kept
/// ALONGSIDE this throw rather than replaced by it, and the two guard
/// different populations: this one covers every value that reaches the
/// composition root, the floor covers every construction that bypasses it
/// (tests today, any future in-code wiring). They are armed separately — see
/// progress/impl_batch_d5_doc_comment_targets_and_dispatch_clamp.md.
/// </remarks>
public sealed class OrdersSagaDispatchValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddOrdersSaga_DegreeOfParallelismBelowOne_ThrowsAtCompositionTime_NamingTheOptionAndTheValue(int configured)
    {
        var services = new ServiceCollection();

        InvalidOperationException? error = null;
        try
        {
            services.AddOrdersSaga(options => options.Dispatch.DegreeOfParallelism = configured);
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        // Deliberately NOT Assert.Throws: it takes no message, so its failure
        // reads "No exception was thrown / Expected: InvalidOperationException"
        // and names nothing — which CLAUDE.md (adopted as id 82) rules is not
        // acceptable arming evidence. Measured: that is exactly what this test
        // printed before it was rewritten this way.
        Assert.True(
            error is not null,
            $"AddOrdersSaga ACCEPTED OrdersSagaOptions.Dispatch.DegreeOfParallelism = {configured} without throwing. A value below 1 must fail " +
            "fast at composition time (CLAUDE.md: DI and configuration failures are loud at boot). At 0, SagaCommandDispatchWorker.ExecuteAsync " +
            "gets no consumer loops, finishes successfully, and leaves the host running and reporting healthy while the saga fast path dispatches " +
            "nothing for any order, forever. Backlog id 90, bullet 3.");

        // The message is part of the guard, not decoration: an operator reading
        // a boot failure must be told WHICH setting and WHICH value.
        Assert.True(
            error!.Message.Contains("DegreeOfParallelism", StringComparison.Ordinal),
            $"the composition-time failure must NAME the option. Message was: {error.Message}");
        Assert.True(
            error.Message.Contains($"configured as {configured}", StringComparison.Ordinal),
            $"the composition-time failure must name the OBSERVED value ({configured}), not merely that some value was wrong. Message was: {error.Message}");
        Assert.True(
            error.Message.Contains("reports healthy", StringComparison.Ordinal),
            $"the composition-time failure must name the CONSEQUENCE (a healthy-looking host that dispatches nothing), which is why this is fatal rather than clamped. Message was: {error.Message}");
    }

    /// <summary>
    /// The control, and the reason the theory above is not vacuous: the
    /// PRODUCTION default (8, supplied by nothing in this test) must pass the
    /// same validation. Without this case, replacing the predicate with an
    /// unconditional <c>throw</c> would leave the theory green while making
    /// every Orders host unbootable.
    /// </summary>
    [Fact]
    public void AddOrdersSaga_AtTheProductionDefault_DoesNotThrow()
    {
        var services = new ServiceCollection();

        services.AddOrdersSaga(_ => { });

        Assert.NotEmpty(services);
    }
}
