using System.Globalization;
using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.CreditDecisions;

/// <summary>
/// Feature 20 — the credit-check SIMULATOR bound in place of
/// <see cref="AlwaysApproveCreditDecision"/> (specs/shared/requirements.md
/// §5.1, R42–R44). A DEMO DETERMINISM DEVICE, not a credit policy: it
/// exists solely to make the compensation path reproducible on demand
/// (place an order whose total ends in <c>.99</c>) without needing a
/// retailer whose credit limit is nearly exhausted. It lives entirely
/// behind <see cref="ICreditDecisionPort"/> — no domain, application or
/// presentation file changes for this feature (billing_credit design.md
/// §6.3, which fixed this exact seam for feature 20).
///
/// <para>
/// Precedence (documented because both rules could apply to the same
/// request): the <c>.99</c> rule is checked FIRST and, if it matches, wins
/// unconditionally — R42 requires the cents rule to reject "regardless of
/// the retailer's available credit" and, by the same demo-determinism
/// intent, regardless of the failure-rate draw too. A <c>.99</c> amount
/// must therefore NEVER surface as <c>simulated_failure_rate</c> depending
/// on which pseudo-random value happened to be drawn — see
/// <c>SimulatorCreditDecisionTests</c> › the precedence test for the guard
/// that pins this down (armed: deleting the ordering, not just the branch,
/// makes it fail).
/// </para>
///
/// <para>
/// No I/O — <see cref="DecideAsync"/> runs inside the credit line's row
/// lock (design.md §5.5). Randomness is injected (<c>random</c>, defaulting
/// to <see cref="Random.Shared"/>'s <c>NextDouble</c>) so the failure-rate
/// branch is deterministic under test — nothing in this class calls
/// <c>Random.Shared.NextDouble()</c> in a way a test cannot override.
/// </para>
/// </summary>
public sealed class SimulatorCreditDecision : ICreditDecisionPort
{
    private readonly double _failureRate;
    private readonly Func<double> _random;

    public SimulatorCreditDecision(double failureRate, Func<double>? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failureRate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(failureRate, 1);

        _failureRate = failureRate;
        _random = random ?? Random.Shared.NextDouble;
    }

    public ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken cancellationToken)
    {
        // R42 — checked FIRST, wins unconditionally when it matches (see
        // the precedence note in this class's header). Deliberately reads
        // ONLY `AmountMinorUnits` — `AvailableCreditMinorUnits` is never
        // consulted on this branch, which is what makes R42's "regardless
        // of the retailer's available credit" a structural property rather
        // than a tested coincidence.
        if (request.AmountMinorUnits % 100 == 99)
        {
            return ValueTask.FromResult<CreditDecision>(new CreditDecision.Refuse(AdapterRejectionReason.SimulatedCentsRule));
        }

        // R43 — a pseudo-random proportion equal to CREDIT_FAILURE_RATE.
        // `_random()` is expected to return a value in [0, 1), the same
        // contract `Random.Shared.NextDouble()` carries; `_failureRate ==
        // 0` (the default) can then never trigger this branch, and
        // `_failureRate == 1` always does.
        if (_random() < _failureRate)
        {
            return ValueTask.FromResult<CreditDecision>(new CreditDecision.Refuse(AdapterRejectionReason.SimulatedFailureRate));
        }

        return ValueTask.FromResult<CreditDecision>(new CreditDecision.Approve());
    }
}

/// <summary>
/// Reads and validates <c>CREDIT_FAILURE_RATE</c> (see <c>.env.example</c> §
/// Billing). Defaults to <c>0</c> so behaviour is deterministic unless a
/// demo explicitly asks for noise (R43). An out-of-range or non-numeric
/// value is NOT clamped and NOT silently defaulted — doing either would
/// make a demo non-reproducible for invisible reasons — it throws
/// synchronously, from inside <c>Program.cs</c>'s <c>configure</c>
/// delegate, so the process never reaches <see
/// cref="Microsoft.Extensions.Hosting.HostApplicationBuilder.Build"/> and
/// the offending raw value is reported in the exception message (R43's
/// last clause).
///
/// <para>
/// Ported-idiom note (CLAUDE.md's ledger): #7 (order-to-cash-nestjs) had
/// to guard this same value with a hand-written "plain decimal numeral"
/// regex BEFORE calling <c>Number()</c>, because JavaScript's <c>Number()</c>
/// silently coerces <c>'0x1'</c> to <c>1</c> and a whitespace-only string to
/// <c>0</c> — both would otherwise be accepted as if they were meaningful.
/// <c>double.TryParse</c> under <see cref="NumberStyles.Float"/> has no such
/// coercion: <c>"0x1"</c>, a whitespace-only string, <c>"abc"</c> and
/// <c>"1,000"</c> all already fail to parse (verified: see
/// <c>progress/impl_billing_credit_simulator.md</c>). So that specific
/// footgun does not exist in .NET and no extra regex is needed here. What
/// DOES carry over unchanged, and IS still guarded below: the literal
/// strings <c>"NaN"</c>, <c>"Infinity"</c> and <c>"-Infinity"</c> parse
/// SUCCESSFULLY to real, non-finite <see cref="double"/> values in .NET
/// (exactly as <c>Number('NaN')</c> does in JavaScript) — <c>TryParse</c>
/// returning <see langword="true"/> is therefore not sufficient on its own,
/// which is why <see cref="double.IsFinite(double)"/> is checked
/// explicitly. Accepting <c>"1e0"</c> and <c>"+0.5"</c> as valid — which #7
/// deliberately rejected for extra demo strictness — is a considered
/// difference: both denote real numbers inside the closed interval, and
/// R43's own wording ("a number in the closed interval [0, 1]") does not
/// ask for a narrower numeral shape than that.
/// </para>
/// </summary>
public static class CreditSimulatorOptionsLoader
{
    public static double Load(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return 0;
        }

        var trimmed = raw.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ||
            !double.IsFinite(rate) ||
            rate < 0 ||
            rate > 1)
        {
            throw new InvalidOperationException($"CREDIT_FAILURE_RATE must be a number in the closed interval [0, 1]; got \"{raw}\".");
        }

        return rate;
    }
}
