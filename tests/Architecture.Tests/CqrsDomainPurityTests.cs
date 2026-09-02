using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// CLAUDE.md, the src/Cqrs non-negotiable added at the Phase 8 human gate —
/// "It does not widen what the domain may reach for. src/Cqrs is an
/// Application-layer concern: handlers live in Application/, and no Domain/
/// namespace may reference OrderToCash.Cqrs. An architecture test enforces
/// that, because nothing else would."
///
/// Scoped like the twelve <see cref="DomainPurityTests"/> rules — over
/// <see cref="DomainAssemblies.All"/> (every service's Domain/ layer union
/// SharedKernel), not like <see cref="OrdersDomainContractsTests"/>'s
/// single-service scope. The two are deliberately different shapes:
/// <c>OrdersDomainMustNotDependOnContracts</c> is Orders' own rule, because a
/// consumer-side domain may legitimately want the wire payload types it
/// reads off the fact stream, and only Orders' domain forwards facts as an
/// outbox writer's input. The Cqrs prohibition has no such per-service
/// exception in CLAUDE.md's wording ("no Domain/ namespace" — not "Orders'
/// Domain/ namespace"), and the reason CLAUDE.md gives is generic to every
/// service: "every service project will reference src/Cqrs from feature 15
/// onward," so the compiler stops nothing, in any of them, from a Domain
/// type reaching for the dispatcher. A DomainAssemblies.All scope is what
/// keeps this rule from silently missing five of the six services once they
/// pick up the Cqrs reference in their own Application/ layers.
/// </summary>
public sealed class CqrsDomainPurityTests
{
    [Fact]
    public void DomainMustNotDependOnCqrs()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("OrderToCash.Cqrs")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on OrderToCash.Cqrs — src/Cqrs is an Application-layer " +
            $"concern; handlers live in Application/. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
