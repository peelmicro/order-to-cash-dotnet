using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// Feature 20 — R42, R43. Pure unit tests, no I/O, deterministic randomness
/// via injection (never <see cref="Random.Shared"/> in an assertion).
/// </summary>
public sealed class SimulatorCreditDecisionTests
{
    private static CreditDecisionRequest Request(long amountMinorUnits = 4_000, long availableCreditMinorUnits = 999_999_999) =>
        new("ORD-000001", "CarrefourEs", "IBERFOODS", "CR-000001", amountMinorUnits, "EUR", availableCreditMinorUnits);

    // ── R42 — the `.99` rule ────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(24_999)]
    [InlineData(500_000)]
    [InlineData(9_223_372_036_854_775_799)] // near long.MaxValue, also ending in 99
    public async Task R42_RejectsAnAmountEndingIn99WithSimulatedCentsRule_RegardlessOfTheAvailableCredit(long availableCredit)
    {
        var adapter = new SimulatorCreditDecision(failureRate: 0, random: () => 0);

        var decision = await adapter.DecideAsync(Request(amountMinorUnits: 24_999, availableCreditMinorUnits: availableCredit), CancellationToken.None);

        var refusal = Assert.IsType<CreditDecision.Refuse>(decision);
        Assert.Equal(AdapterRejectionReason.SimulatedCentsRule, refusal.Reason);
    }

    [Theory]
    [InlineData(98)]
    [InlineData(100)]
    [InlineData(24_909)]
    [InlineData(24_998)]
    [InlineData(25_000)]
    [InlineData(0)]
    public async Task R42_DoesNotFireForAnAmountThatDoesNotEndIn99(long amountMinorUnits)
    {
        var adapter = new SimulatorCreditDecision(failureRate: 0, random: () => 0);

        var decision = await adapter.DecideAsync(Request(amountMinorUnits: amountMinorUnits), CancellationToken.None);

        Assert.IsType<CreditDecision.Approve>(decision);
    }

    [Fact]
    public async Task R42_ApprovesAnAmountThatDoesNotEndIn99_WhenTheFailureRateIsZero()
    {
        var adapter = new SimulatorCreditDecision(failureRate: 0, random: () => 0);

        var decision = await adapter.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None);

        Assert.IsType<CreditDecision.Approve>(decision);
    }

    /// <summary>
    /// R42's precedence guarantee, checked as an ORDERING property, not
    /// merely as "a `.99` amount is rejected": `failureRate = 1` and
    /// `random() = 0` would ALSO refuse via `simulated_failure_rate` if the
    /// cents check did not run FIRST. A test that only asserted the
    /// eventual `Refuse` would pass even with the two checks swapped, most
    /// of the time — this fixes the failure-rate branch to its
    /// certain-to-fire configuration so ordering is the only thing that can
    /// make it pass.
    /// </summary>
    [Fact]
    public async Task TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply()
    {
        var adapter = new SimulatorCreditDecision(failureRate: 1, random: () => 0);

        var decision = await adapter.DecideAsync(Request(amountMinorUnits: 24_999), CancellationToken.None);

        var refusal = Assert.IsType<CreditDecision.Refuse>(decision);
        Assert.Equal(AdapterRejectionReason.SimulatedCentsRule, refusal.Reason);
    }

    // ── R43 — the failure rate ───────────────────────────────────────────

    [Fact]
    public async Task R43_RejectsWithSimulatedFailureRate_OnlyWhenTheDrawFallsBelowTheConfiguredRate_AndNeverAtAZeroRate()
    {
        var belowThreshold = new SimulatorCreditDecision(failureRate: 0.5, random: () => 0.4);
        var atThreshold = new SimulatorCreditDecision(failureRate: 0.5, random: () => 0.5);
        var zeroRateAtLowestDraw = new SimulatorCreditDecision(failureRate: 0, random: () => 0);

        var belowDecision = Assert.IsType<CreditDecision.Refuse>(await belowThreshold.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));
        Assert.Equal(AdapterRejectionReason.SimulatedFailureRate, belowDecision.Reason);

        Assert.IsType<CreditDecision.Approve>(await atThreshold.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));
        Assert.IsType<CreditDecision.Approve>(await zeroRateAtLowestDraw.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));
    }

    [Fact]
    public async Task R43_ALoosenedBoundaryComparison_WouldHaveApprovedAtExactlyTheThreshold_SoTheComparisonMustBeStrict()
    {
        // Documents the exact boundary the comparison must respect: `draw <
        // rate` refuses, `draw == rate` does not. Both zero and mid-range
        // rates discriminate at their own boundary.
        var atLowestDrawZeroRate = new SimulatorCreditDecision(failureRate: 0, random: () => 0);
        var atMidRateBoundary = new SimulatorCreditDecision(failureRate: 0.5, random: () => 0.5);

        Assert.IsType<CreditDecision.Approve>(await atLowestDrawZeroRate.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));
        Assert.IsType<CreditDecision.Approve>(await atMidRateBoundary.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));
    }

    [Fact]
    public async Task R43_AFailureRateOfOneRejectsEveryNonCentsAmount()
    {
        var adapter = new SimulatorCreditDecision(failureRate: 1, random: () => 0.999);

        var decision = Assert.IsType<CreditDecision.Refuse>(await adapter.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None));

        Assert.Equal(AdapterRejectionReason.SimulatedFailureRate, decision.Reason);
    }

    /// <summary>
    /// A probabilistic guard is not a guard — this MEASURES the proportion
    /// with an injected, deterministic generator (never <see
    /// cref="Random.Shared"/>) over 200,000 draws per configured rate, the
    /// same instrument #7's reviewer used. Every observation must land
    /// within one percentage point of the configured rate, `rate = 0` must
    /// refuse NOTHING, and `rate = 1` must refuse EVERYTHING.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.75)]
    [InlineData(1)]
    public async Task R43_TheConfiguredFailureRateIsMeasuredOverTwoHundredThousandDeterministicDraws(double rate)
    {
        const int draws = 200_000;
        var generator = new Random(Seed: 20260906);
        var adapter = new SimulatorCreditDecision(failureRate: rate, random: generator.NextDouble);
        var refused = 0;

        for (var i = 0; i < draws; i++)
        {
            var decision = await adapter.DecideAsync(Request(amountMinorUnits: 25_000), CancellationToken.None);
            if (decision is CreditDecision.Refuse refusal)
            {
                Assert.Equal(AdapterRejectionReason.SimulatedFailureRate, refusal.Reason);
                refused++;
            }
        }

        var observed = (double)refused / draws;

        if (rate == 0)
        {
            Assert.Equal(0, refused);
        }
        else if (rate == 1)
        {
            Assert.Equal(draws, refused);
        }
        else
        {
            Assert.InRange(observed, rate - 0.01, rate + 0.01);
        }
    }

    // ── No I/O, synchronous ──────────────────────────────────────────────

    [Fact]
    public void NeverPerformsIOAndCompletesSynchronously()
    {
        var adapter = new SimulatorCreditDecision(failureRate: 0);

        var result = adapter.DecideAsync(Request(), CancellationToken.None);

        Assert.True(result.IsCompletedSuccessfully);
    }

    // ── Constructor guard (defense in depth beyond the loader) ──────────

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    public void ConstructorRejectsAFailureRateOutsideTheClosedIntervalZeroToOne(double invalidRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulatorCreditDecision(invalidRate));
    }
}

/// <summary>Feature 20 — R43's start-up validation of `CREDIT_FAILURE_RATE`.</summary>
public sealed class CreditSimulatorOptionsLoaderTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("0.25", 0.25)]
    public void LoadsTheConfiguredRate_DefaultingToZeroWhenAbsentOrEmpty(string? raw, double expected)
    {
        Assert.Equal(expected, CreditSimulatorOptionsLoader.Load(raw));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    [InlineData("not-a-number")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("0x1")]
    [InlineData("  ")]
    [InlineData("1,000")]
    public void FailsToStart_ReportingTheOffendingValue_ForAnythingOutsideTheClosedIntervalZeroToOneOrNotANumber(string raw)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreditSimulatorOptionsLoader.Load(raw));

        Assert.Contains(raw, exception.Message, StringComparison.Ordinal);
        Assert.Contains("CREDIT_FAILURE_RATE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[0, 1]", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both closed-interval endpoints are accepted — R43 says "the closed
    /// interval `[0, 1]`", so neither boundary may be treated as
    /// out-of-range.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void AcceptsBothEndpointsOfTheClosedInterval(string raw)
    {
        var rate = CreditSimulatorOptionsLoader.Load(raw);
        Assert.True(rate is 0 or 1);
    }
}

/// <summary>
/// R43's last clause proved at the level Program.cs actually exercises it:
/// <see cref="BillingHost.CreateBuilder"/>'s <c>configure</c> delegate is
/// exactly where Program.cs calls <see cref="CreditSimulatorOptionsLoader.Load"/>,
/// so an invalid value must fail HERE, before <c>Build()</c> — never only
/// inside the loader function in isolation. No real MS-SQL/NATS/Kafka
/// needed: the exception is thrown while `configure(options)` runs, before
/// <c>AddBilling</c> even registers <c>AddDbContext</c> (the same
/// no-real-infra shape <c>BillingDispatcherRegistrationTests</c> uses).
/// </summary>
public sealed class BillingHostCreditFailureRateBootTests
{
    [Fact]
    public void CreateBuilder_ThrowsBeforeBuild_WhenTheConfigureDelegateLoadsAnInvalidCreditFailureRate()
    {
        var exception = Record.Exception(() =>
            OrderToCash.Billing.BillingHost.CreateBuilder(
                args: [],
                configure: options =>
                {
                    options.ConnectionString = "Server=localhost;Database=otc_billing_credit_failure_rate_probe;Trusted_Connection=True;";
                    options.Nats.Url = "nats://127.0.0.1:1";
                    options.Kafka.BootstrapServers = "127.0.0.1:1";
                    options.CreditFailureRate = CreditSimulatorOptionsLoader.Load("not-a-number");
                }));

        var invalidOperation = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("CREDIT_FAILURE_RATE", invalidOperation.Message, StringComparison.Ordinal);
        Assert.Contains("not-a-number", invalidOperation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateBuilder_ThenBuild_SucceedsWithTheDefaultCreditFailureRateOfZero()
    {
        var builder = OrderToCash.Billing.BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = "Server=localhost;Database=otc_billing_credit_failure_rate_probe_ok;Trusted_Connection=True;";
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
                options.CreditFailureRate = CreditSimulatorOptionsLoader.Load(null);
            });

        var exception = Record.Exception(() => builder.Build());

        Assert.Null(exception);
    }
}
