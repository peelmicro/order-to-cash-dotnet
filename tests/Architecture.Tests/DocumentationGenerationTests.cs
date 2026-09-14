using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Backlog id 78. <c>Directory.Build.props</c> set
/// <c>GenerateDocumentationFile=false</c>, and while it did, Roslyn never
/// bound a <c>&lt;see cref="..."/&gt;</c> at all: CS1574, CS1584, CS1734,
/// CS0419 and CS1570 were never emitted, so every "the build verifies the doc
/// comments" claim in this repository was a guard that could not fire, and a
/// broken cref survived a full non-incremental build under
/// <c>TreatWarningsAsErrors</c>. Turning the property on surfaced 107 real
/// sites across all 28 projects in one pass.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test reads the build OUTPUT and never
/// <c>Directory.Build.props</c>.</b> Asserting that the props file contains
/// the string <c>&lt;GenerateDocumentationFile&gt;true&lt;/…&gt;</c> would be
/// CLAUDE.md's defeat-list attack #8 — comparing a literal to a literal. It
/// would pass on a file MSBuild never evaluated, on a property overridden in
/// a <c>.csproj</c>, on a property overridden on the command line, and on a
/// value MSBuild resolved differently from how a text scanner read it. What
/// the entry is about is whether the COMPILER is binding crefs, and the only
/// direct evidence of that is the artefact the compiler emits when it does:
/// the XML documentation file beside each assembly. So the assertion is
/// "the compiler produced a documentation file for this assembly", read off
/// <see cref="Assembly.Location"/>.
/// </para>
/// <para>
/// <b>Known bound, stated rather than hidden.</b> A stale
/// <c>*.xml</c> left in <c>bin/</c> from an earlier documentation-enabled
/// build would satisfy <see cref="File.Exists(string)"/> on its own, so this
/// test also requires the file to be a well-formed
/// <c>&lt;doc&gt;&lt;assembly&gt;&lt;name&gt;</c> naming THIS assembly —
/// which a stale file from a different project cannot do. It cannot tell a
/// stale file of the SAME assembly from a fresh one; MSBuild's incremental
/// clean removes outputs a project no longer writes, which was verified by
/// flipping the property and observing the files disappear (see this
/// feature's arming table, arm A2).
/// </para>
/// </remarks>
public sealed class DocumentationGenerationTests
{
    /// <summary>
    /// The population is a LITERAL list, never derived from "assemblies that
    /// happen to have a documentation file" — a set defined by the property
    /// under test would let a violation remove itself from the population
    /// instead of failing (CLAUDE.md: "make the expected set a literal and
    /// derive the rest by subtraction"). Every production assembly in the
    /// solution, plus this test assembly, so the claim covers both halves of
    /// the repository and not only <c>src/</c>.
    /// </summary>
    private static readonly (string Name, Assembly Assembly)[] _covered =
    [
        ("OrderToCash.SharedKernel", typeof(OrderToCash.SharedKernel.Money).Assembly),
        ("OrderToCash.Contracts", typeof(OrderToCash.Contracts.Envelopes.Envelope<object>).Assembly),
        ("OrderToCash.Cqrs", typeof(OrderToCash.Cqrs.IDispatcher).Assembly),
        ("OrderToCash.Gateway", typeof(OrderToCash.Gateway.GatewayHost).Assembly),
        ("OrderToCash.Orders", typeof(OrderToCash.Orders.OrdersHost).Assembly),
        ("OrderToCash.Fulfillment", typeof(OrderToCash.Fulfillment.FulfillmentHost).Assembly),
        ("OrderToCash.Billing", typeof(OrderToCash.Billing.BillingHost).Assembly),
        ("OrderToCash.Notifications", typeof(OrderToCash.Notifications.NotificationsHost).Assembly),
        ("OrderToCash.Projector", typeof(OrderToCash.Projector.Domain.ProjectorDomainPlaceholder).Assembly),
        ("OrderToCash.Seed", typeof(OrderToCash.Seed.Domain.SeedDomainPlaceholder).Assembly),
        ("OrderToCash.Architecture.Tests", typeof(DocumentationGenerationTests).Assembly),
    ];

    [Fact]
    public void EveryCoveredAssembly_HasACompilerEmittedXmlDocumentationFile_SoBrokenSeeCrefTargetsCannotSurviveTheBuild()
    {
        var failures = new List<string>();

        foreach (var (name, assembly) in _covered)
        {
            var expected = Path.ChangeExtension(assembly.Location, ".xml");

            if (!File.Exists(expected))
            {
                failures.Add(
                    $"{name}: GenerateDocumentationFile is OFF for this assembly — the compiler emitted no XML " +
                    $"documentation file at '{expected}'. While it is off, Roslyn does not bind <see cref=\"...\"/> " +
                    "targets at all, so CS1574/CS1584/CS1734/CS0419/CS1570 are never emitted and every broken " +
                    "doc-comment target in this assembly survives the build silently.");
                continue;
            }

            string? declared;
            try
            {
                declared = XDocument.Load(expected).Root?.Element("assembly")?.Element("name")?.Value;
            }
            catch (System.Xml.XmlException ex)
            {
                failures.Add($"{name}: '{expected}' is not a well-formed XML documentation file — {ex.Message}");
                continue;
            }

            if (!string.Equals(declared, name, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{name}: '{expected}' exists but declares <assembly><name>{declared ?? "(absent)"}</name>, " +
                    $"not '{name}' — this is not the documentation file the compiler emits for this assembly.");
            }
        }

        Assert.True(
            failures.Count == 0,
            "GenerateDocumentationFile must stay ON for every project (Directory.Build.props, backlog id 78): it is " +
            "the ONLY thing that makes a broken <see cref> target fail the build, and TreatWarningsAsErrors is what " +
            "turns the resulting warning into an error. " + failures.Count + " of " + _covered.Length +
            " covered assemblies failed:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
