using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Guards against <see cref="DomainAssemblies"/> — and the domain-namespace
/// selector every purity/decimal rule builds on — becoming vacuous. A rule
/// that "passes" because it selected zero types is worthless; this test
/// exists so a future edit that drops a service from
/// <see cref="DomainAssemblies.All"/>, or renames its Domain/ namespace,
/// fails loudly instead of leaving every other architecture rule silently
/// passing over a shrinking (or empty) type set. See
/// progress/review_monorepo_scaffold.md, defect D2.
///
/// Eight assemblies, not seven: the seven services plus SharedKernel.
/// SharedKernel is whole-assembly-domain (see the comment on
/// <see cref="DomainAssemblies"/> for why) — it was missing from this list
/// entirely until the leader caught it during feature 7's review, at which
/// point every purity/decimal rule had been silently vacuous over
/// <c>Money</c>, <c>Quantity</c>, <c>GLN</c>, <c>Entity</c>,
/// <c>AggregateRoot</c> and <c>DomainError</c> since this assembly was
/// created. Do not shrink this list back to seven to make it "just the
/// services" again — that reintroduces exactly that gap.
/// </summary>
public sealed class DomainAssembliesTests
{
    private static readonly string[] _expectedAssemblyNames =
    [
        "OrderToCash.Gateway",
        "OrderToCash.Orders",
        "OrderToCash.Fulfillment",
        "OrderToCash.Billing",
        "OrderToCash.Notifications",
        "OrderToCash.Projector",
        "OrderToCash.Seed",
        "OrderToCash.SharedKernel",
    ];

    [Fact]
    public void DomainAssembliesAllContainsExactlyTheSevenServicesPlusSharedKernel()
    {
        var actualNames = DomainAssemblies.All
            .Select(a => a.GetName().Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var expectedNames = _expectedAssemblyNames
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expectedNames.SequenceEqual(actualNames),
            $"DomainAssemblies.All must contain exactly {{{string.Join(", ", expectedNames)}}}, " +
            $"found {{{string.Join(", ", actualNames)}}}.");
    }

    [Fact]
    public void DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly()
    {
        var emptyAssemblies = new List<string>();

        foreach (var assembly in DomainAssemblies.All)
        {
            var matchedTypes = Types.InAssembly(assembly)
                .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
                .GetTypes();

            if (!matchedTypes.Any())
            {
                emptyAssemblies.Add(assembly.GetName().Name ?? assembly.FullName ?? "<unknown>");
            }
        }

        Assert.True(
            emptyAssemblies.Count == 0,
            "The domain-namespace selector (DomainAssemblies.DomainNamespacePattern) must select at " +
            "least one type in every assembly in DomainAssemblies.All — every domain-purity and " +
            "decimal rule in this project is worthless over an empty selection. Assemblies with zero " +
            $"matching types: {string.Join(", ", emptyAssemblies)}");
    }
}
