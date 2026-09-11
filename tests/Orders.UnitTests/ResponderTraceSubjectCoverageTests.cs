using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// A3c, design.md §5.2 — the three RPC responder classes serve FIFTEEN
/// subjects between them (3 + 6 + 6), each with exactly one trace
/// EXTRACTION site (design.md §5.2's own count). The subject list is read
/// from each responder's own subscription loop and resolved against the
/// real <c>*Subjects</c> constant declarations — never re-typed — so a
/// SUBSTITUTION (one subscription repointed at another real subject) is
/// caught as a drop in the DISTINCT subject count, not merely a token-name
/// count.
/// </summary>
public sealed partial class ResponderTraceSubjectCoverageTests
{
    [Fact]
    public void A3c_TheThreeRespondersServeExactlyFifteenDistinctSubjects_EachWithOneExtractionSiteInItsOwnClass()
    {
        var root = RepositoryPaths.Find(string.Empty);

        var subjectValues = ReadSubjectConstants(root,
            "src/Orders/Infrastructure/Messaging/Rpc/RpcSubjects.cs",
            "src/Fulfillment/Presentation/Rpc/StockSubjects.cs",
            "src/Billing/Presentation/Rpc/CreditSubjects.cs",
            "src/Billing/Presentation/Rpc/InvoiceSubjects.cs");

        var ordersTokens = ExtractLoopSubjectTokens(root, "src/Orders/Presentation/OrdersCreateResponder.cs");
        var stockTokens = ExtractLoopSubjectTokens(root, "src/Fulfillment/Presentation/StockRpcResponder.cs");
        var billingTokens = ExtractLoopSubjectTokens(root, "src/Billing/Presentation/BillingRpcResponder.cs");

        // The 3 + 6 + 6 = 15 countable claim, per responder class.
        Assert.Equal(3, ordersTokens.Count);
        Assert.Equal(6, stockTokens.Count);
        Assert.Equal(6, billingTokens.Count);

        var allTokens = ordersTokens.Concat(stockTokens).Concat(billingTokens).ToList();
        Assert.Equal(15, allTokens.Count);

        var resolvedSubjects = allTokens.Select(token =>
        {
            Assert.True(subjectValues.TryGetValue(token, out var value), $"Token '{token}' has no matching const in any of the *Subjects.cs files this test read.");
            return value!;
        }).ToList();

        // The SUBSTITUTION guard: fifteen TOKENS resolving to fewer than
        // fifteen DISTINCT real subjects means one subscription was
        // repointed at another real subject already served elsewhere.
        var distinctSubjects = resolvedSubjects.Distinct(StringComparer.Ordinal).ToList();
        Assert.True(
            distinctSubjects.Count == 15,
            $"Expected 15 DISTINCT subjects; found {distinctSubjects.Count}: {string.Join(", ", resolvedSubjects)}.");
    }

    /// <summary>
    /// Every DISTINCT <c>XxxSubjects.Yyy</c> token passed to the ACTUAL
    /// SUBSCRIBE call — <c>connection.SubscribeAsync&lt;byte[]&gt;(TOKEN, ...)</c>
    /// (Orders' three separate loop methods) or <c>SubscribeLoopAsync(TOKEN,
    /// stoppingToken)</c> (Fulfillment's/Billing's <c>ExecuteAsync</c> array)
    /// — deliberately NOT a whole-file scan: a subject token also appears in
    /// the dispatch switch and in <c>RequireMeta</c> calls, so a whole-file
    /// scan cannot see a subscription silently repointed at another real
    /// subject while those other references stay untouched (found live
    /// while arming this exact mutation — see the implementation record).
    /// </summary>
    private static List<string> ExtractLoopSubjectTokens(string root, string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(root, relativePath));
        return SubscribeCallSubjectRegex().Matches(text).Select(m => m.Groups["token"].Value).ToList();
    }

    private static Dictionary<string, string> ReadSubjectConstants(string root, params string[] relativePaths)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relativePath in relativePaths)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            var className = ClassNameRegex().Match(text).Groups["name"].Value;

            foreach (Match match in ConstDeclarationRegex().Matches(text))
            {
                values[$"{className}.{match.Groups["name"].Value}"] = match.Groups["value"].Value;
            }
        }

        return values;
    }

    [GeneratedRegex(@"(?:connection\.SubscribeAsync<byte\[\]>\(|SubscribeLoopAsync\()(?<token>(?:RpcSubjects|StockSubjects|CreditSubjects|InvoiceSubjects)\.[A-Za-z0-9_]+)")]
    private static partial Regex SubscribeCallSubjectRegex();

    [GeneratedRegex(@"public static class (?<name>[A-Za-z0-9_]+)")]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex("public const string (?<name>[A-Za-z0-9_]+) = \"(?<value>[^\"]+)\";")]
    private static partial Regex ConstDeclarationRegex();
}
