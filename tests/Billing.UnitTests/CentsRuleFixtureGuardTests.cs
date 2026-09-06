using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI17` — both halves of the `.99` fixture guard. The COMPUTED case is
/// load-bearing: no scan of literals would ever see `3 × 8_333 = 24_999`.
/// </summary>
public sealed class CentsRuleFixtureGuardTests
{
    [Fact]
    public void RefusesAFixtureAmountThatWouldTriggerTheSimulatedCentsRuleUnlessTheFixtureOptsIn_IncludingAComputedTotalOfThreeTimes8333()
    {
        var computedTotal = 3 * 8_333;
        Assert.Equal(24_999, computedTotal);

        Assert.Throws<InvalidOperationException>(() => CentsRuleFixtureGuard.AssertNotCentsRuleAmount(computedTotal, "test fixture"));

        // Opting in explicitly passes.
        var exception = Record.Exception(() => CentsRuleFixtureGuard.AssertNotCentsRuleAmount(computedTotal, "test fixture", CentsRuleFixtureGuard.OptIn));
        Assert.Null(exception);

        // A non-.99 amount never throws.
        exception = Record.Exception(() => CentsRuleFixtureGuard.AssertNotCentsRuleAmount(25_000, "test fixture"));
        Assert.Null(exception);
    }

    [Fact]
    public void ScansEveryBillingIntegrationTestForAnUnOptedInAmountEndingIn99_AndReportsZeroHits()
    {
        var testRoot = RepositoryPaths.Find(Path.Combine("tests", "Billing.IntegrationTests"));

        var hits = CentsRuleFixtureGuard.FindUnguardedLiterals(testRoot);

        Assert.Empty(hits);
    }

    /// <summary>Non-vacuity — the scan must be PROVED to fire against a scratch fixture, never merely asserted empty and believed (`CLAUDE.md`: a negative claim about the repository is a search result).</summary>
    [Fact]
    public void TheScanGenuinelyFiresAgainstAScratchFixtureRatherThanAssertingEmptinessVacuously()
    {
        var testRoot = RepositoryPaths.Find(Path.Combine("tests", "Billing.IntegrationTests"));
        var scratchFile = Path.Combine(testRoot, $"__ScratchCentsRuleFixture_{Guid.NewGuid():N}.cs");

        try
        {
            File.WriteAllLines(scratchFile,
            [
                "namespace OrderToCash.Billing.IntegrationTests;",
                "internal static class ScratchFixtureProbe",
                "{",
                "    public static readonly long UnguardedAmount = 24_999;",
                "}",
            ]);

            var hits = CentsRuleFixtureGuard.FindUnguardedLiterals(testRoot);

            var scratchHits = hits.Where(h => h.File == scratchFile).ToList();
            Assert.Single(scratchHits);
            Assert.Equal(4, scratchHits[0].Line);
            Assert.Equal(24_999, scratchHits[0].Value);
        }
        finally
        {
            File.Delete(scratchFile);
        }
    }
}
