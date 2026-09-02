using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// design.md §11.2 (feature <c>orders_aggregate</c>) — <c>OrdersDomainMustNotDependOnContracts</c>:
/// no type in <c>OrderToCash.Orders.Domain*</c> may depend on
/// <c>OrderToCash.Contracts</c>.
/// </summary>
/// <remarks>
/// <c>Orders.csproj</c> references <c>Contracts</c> for its
/// <c>Infrastructure/</c> layer, so the compiler does not stop the domain
/// from reaching for a wire type — nothing does today (a scan of
/// <c>src/Orders/Domain/</c> finds zero <c>OrderToCash.Contracts</c>
/// references), but nothing prevented it either. <c>Contracts</c> is
/// versioned by <c>asyncapi.yaml</c> and shaped by <c>JsonWire</c>'s
/// serializer options; a domain type that referenced it would make a wire
/// change a domain change. The mapping from a domain event to an
/// <c>Envelope&lt;TPayload&gt;</c> belongs to feature 14's outbox writer, in
/// <c>Infrastructure/</c> — not here.
///
/// Scoped to <c>OrderToCash.Orders.Domain*</c> deliberately, unlike the
/// twelve <see cref="DomainPurityTests"/> rules, which run over every
/// service's domain layer via <see cref="DomainAssemblies.All"/>. A
/// consumer-side domain such as the projector's may legitimately want the
/// payload types it reads off the fact stream — this rule is Orders' own,
/// not a thirteenth entry in that shared family.
/// </remarks>
public sealed class OrdersDomainContractsTests
{
    private const string OrdersDomainNamespacePattern = @"^OrderToCash\.Orders\.Domain(\.|$)";

    [Fact]
    public void OrdersDomainMustNotDependOnContracts()
    {
        var result = Types.InAssembly(typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder).Assembly)
            .That().ResideInNamespaceMatching(OrdersDomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("OrderToCash.Contracts")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"OrderToCash.Orders.Domain types must not depend on OrderToCash.Contracts. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
