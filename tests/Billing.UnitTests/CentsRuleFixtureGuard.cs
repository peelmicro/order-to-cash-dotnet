using System.Text.RegularExpressions;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// The runtime half of `BI17` (design.md §10.1) — test-support, NOT a test.
/// `review_billing_credit_simulator.md`'s `N2` asked for a durable guard
/// against an integration fixture's amount accidentally tripping the
/// simulated `.99` cents rule; feature 20 shipped only the search and the
/// prose. This closes it: <see cref="AssertNotCentsRuleAmount"/> is called
/// by every <c>BillingHostFixture</c> builder whose amount can reach the
/// credit-decision port, on the amount actually COMPUTED — the hazard a
/// scan of literals cannot see, since `3 × 8_333 = 24_999` never appears as
/// a literal anywhere. <see cref="FindUnguardedLiterals"/> is the text-scan
/// backstop for hand-assembled payloads that never reach a builder.
/// </summary>
/// <remarks>
/// Duplicated byte-for-byte into `tests/Billing.IntegrationTests/CentsRuleFixtureGuard.cs`
/// (never referenced across the two test projects) — the same
/// <c>SqlExceptionFactory.cs</c> shape this repository already uses rather
/// than a `Billing.UnitTests` → `Billing.IntegrationTests` project
/// reference, which would pull Testcontainers/Confluent.Kafka/NATS.Net into
/// a project whose entire point is to run with none of them. Keep both
/// copies identical; <c>CentsRuleFixtureGuardTests.cs</c> exercises the
/// `Billing.UnitTests` copy and stands in for both.
/// </remarks>
internal static partial class CentsRuleFixtureGuard
{
    /// <summary>The inline escape hatch — a `// cents-rule-intentional` comment on the offending line, never a file allow-list (which would rot on a rename).</summary>
    public const string OptIn = "cents-rule-intentional";

    private const string MarkerComment = "// " + OptIn;

    /// <summary>Throws unless <paramref name="minorUnits"/> mod 100 != 99, or the caller passed <see cref="OptIn"/> explicitly.</summary>
    public static void AssertNotCentsRuleAmount(long minorUnits, string context, string? optIn = null)
    {
        if (string.Equals(optIn, OptIn, StringComparison.Ordinal))
        {
            return;
        }

        if (minorUnits % 100 != 99)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{context}: the amount {minorUnits} ends in 99 (mod 100), which the simulated credit-decision cents rule (R42) refuses unconditionally regardless of available credit. " +
            $"If this is deliberate, pass optIn: {nameof(CentsRuleFixtureGuard)}.{nameof(OptIn)}.");
    }

    /// <summary>
    /// Scans every `.cs` file directly under <paramref name="testRoot"/> for
    /// an integer literal in a money position (comment-stripped) whose value
    /// mod 100 is 99, honouring an inline <see cref="MarkerComment"/> on the
    /// offending line. A "money position" is approximated as a numeric
    /// literal token not embedded in an identifier or a string literal —
    /// digits immediately preceded by a letter, digit, underscore, quote or
    /// hyphen (an order reference's `-000099` suffix, or part of an
    /// identifier like `EndingIn99`) are excluded.
    /// </summary>
    public static IReadOnlyList<(string File, int Line, long Value)> FindUnguardedLiterals(string testRoot)
    {
        var hits = new List<(string File, int Line, long Value)>();

        foreach (var file in Directory.GetFiles(testRoot, "*.cs", SearchOption.TopDirectoryOnly))
        {
            // Never scan the guard's own support file: its logic legitimately
            // reads "% 100 != 99" and similar — that is the RULE being
            // implemented, not a fixture amount in a money position.
            if (string.Equals(Path.GetFileName(file), $"{nameof(CentsRuleFixtureGuard)}.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains(MarkerComment, StringComparison.Ordinal))
                {
                    continue;
                }

                var codeOnly = line.Split("//", 2)[0];
                foreach (Match match in MoneyPositionLiteral().Matches(codeOnly))
                {
                    var raw = match.Value.Replace("_", string.Empty, StringComparison.Ordinal);
                    if (long.TryParse(raw, out var value) && value % 100 == 99)
                    {
                        hits.Add((file, i + 1, value));
                    }
                }
            }
        }

        return hits;
    }

    [GeneratedRegex("""(?<![\w"'-])\d[\d_]*(?!\w)""")]
    private static partial Regex MoneyPositionLiteral();
}
