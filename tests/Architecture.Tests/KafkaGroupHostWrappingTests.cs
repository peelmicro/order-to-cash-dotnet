using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Backlog id 74 bullet 6 (advisory A16) — the CENSUS that keeps the
/// structural consumer-group clearance structural.
///
/// <para>
/// <c>KafkaGroupTestHost</c>-style wrappers make a bare
/// <c>host.StopAsync()</c> clear the group, but only for hosts that are
/// actually wrapped. A future test that builds its own host from
/// <c>OrdersHost.CreateBuilder</c> / <c>NotificationsHost.CreateBuilder</c> /
/// <c>ProjectorHost.CreateBuilder</c> and forgets the wrapper reintroduces the
/// escape. Those three composition roots ALWAYS register their service's
/// <c>KafkaFactStreamSubscriber</c> and its consumer
/// (<c>src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:44,97</c>,
/// <c>src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs:67,105</c>,
/// <c>src/Projector/Infrastructure/ProjectorServiceCollectionExtensions.cs:63,66</c>),
/// so building one IS joining the shared group — that is what makes this a
/// content-determined population rather than a guess. A bare
/// <c>Host.CreateApplicationBuilder()</c> host is deliberately NOT in it:
/// <c>SagaConsumptionTests</c>' own first host registers
/// <c>AddOrdersOutbox</c> + <c>AddOrdersAcceptance</c> only and never joins
/// <c>orders.saga</c> at all.
/// </para>
///
/// <para>
/// The instrument is Roslyn, not a text scan, so a mention inside a comment, a
/// string literal or a <c>#if false</c> region cannot satisfy or trip it. Its
/// premises, stated because changing an instrument swaps one set for another:
/// it parses with <c>DEBUG</c> defined (the build defines it, so the parser and
/// the compiler agree on which region is live), and it looks for the wrapper
/// inside the SAME enclosing method body as the <c>CreateBuilder</c> call — not
/// anywhere in the file — so a wrapper constructed in some other method cannot
/// vouch for this one.
/// </para>
/// </summary>
public sealed class KafkaGroupHostWrappingTests
{
    /// <summary>The three composition roots that unconditionally join a shared Kafka consumer group.</summary>
    private static readonly string[] _groupJoiningComposers =
    [
        "OrdersHost",
        "NotificationsHost",
        "ProjectorHost",
    ];

    private static readonly string[] _scannedProjects =
    [
        "Orders.IntegrationTests",
        "Notifications.IntegrationTests",
        "Projector.IntegrationTests",
    ];

    /// <summary>
    /// The LITERAL exemption set — a build site whose host is deliberately
    /// never started into the group. Adding to it is a decision someone has to
    /// write down, which is the point.
    /// </summary>
    private static readonly Dictionary<string, string> _exempt = new(StringComparer.Ordinal)
    {
        ["Projector.IntegrationTests/ProjectorBootTests.cs::PR45_AnUnreachableNatsUrl_FailsTheHostStart_RatherThanRunningWithEverySignalSwallowed"] =
            "the host's StartAsync is asserted to THROW (an unreachable NATS url), so its Kafka consumer never subscribes and no group membership is ever created.",
    };

    [Fact]
    public void EveryTestHostBuiltFromAGroupJoiningCompositionRoot_IsWrappedForConsumerGroupClearance()
    {
        var repositoryRoot = Path.GetDirectoryName(RepositoryPaths.Find("Directory.Build.props"))!;
        var sites = new List<(string Key, bool Wrapped)>();

        foreach (var project in _scannedProjects)
        {
            var projectRoot = Path.Combine(repositoryRoot, "tests", project);

            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories).Where(f => !IsUnderBuildOutput(f)))
            {
                // DEBUG is defined by the build; parsing without it would make
                // the parser and the compiler disagree about which region is
                // live, which is how a decoy hides.
                var tree = CSharpSyntaxTree.ParseText(
                    File.ReadAllText(file),
                    new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: ["DEBUG"]));

                foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                        member.Name.Identifier.ValueText != "CreateBuilder" ||
                        !_groupJoiningComposers.Contains(LastIdentifierOf(member.Expression), StringComparer.Ordinal))
                    {
                        continue;
                    }

                    var enclosing = invocation.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
                    var methodName = enclosing switch
                    {
                        MethodDeclarationSyntax m => m.Identifier.ValueText,
                        LocalFunctionStatementSyntax l => l.Identifier.ValueText,
                        _ => "<file scope>",
                    };

                    var wrapped = enclosing is not null &&
                        enclosing.DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Any(c => LastIdentifierOf(c.Type) == "KafkaGroupTestHost");

                    sites.Add(($"{project}/{Path.GetRelativePath(projectRoot, file)}::{methodName}", wrapped));
                }
            }
        }

        Assert.True(
            sites.Count > 0,
            "the census found NO host built from a group-joining composition root in any of the three integration-test projects. "
            + "That cannot be right and means the census stopped looking at what it is about — a sweep that finds nothing cannot fail.");

        var unwrapped = sites
            .Where(s => !s.Wrapped && !_exempt.ContainsKey(s.Key))
            .Select(s => s.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unwrapped.Count == 0,
            $"{unwrapped.Count} of {sites.Count} test host(s) built from a composition root that JOINS a shared Kafka consumer group are not "
            + $"wrapped in a KafkaGroupTestHost: {string.Join("; ", unwrapped)}. An unwrapped host can be torn down with a bare "
            + "host.StopAsync(), which leaves a stale member in the group and blocks the NEXT test's rebalance (advisory A16). Wrap it, or "
            + "add it to this guard's literal exemption set with the reason its host never joins the group.");

        // The exemption set may not rot: an entry naming a site that no longer
        // exists would silently widen the guard for the next one added.
        var stale = _exempt.Keys.Except(sites.Select(s => s.Key), StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(
            stale.Count == 0,
            $"{stale.Count} exemption(s) in this guard name a build site that no longer exists: {string.Join("; ", stale)}. "
            + "Remove them — a stale exemption is an unreviewed hole.");
    }

    private static string LastIdentifierOf(SyntaxNode node) => node switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        _ => node.ToString(),
    };

    /// <summary>Attack 10 — excluded BY PATH SEGMENT, never by matching the candidate line's content.</summary>
    private static bool IsUnderBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.Ordinal) ||
                string.Equals(segment, "obj", StringComparison.Ordinal) ||
                string.Equals(segment, "publish", StringComparison.Ordinal));
}
