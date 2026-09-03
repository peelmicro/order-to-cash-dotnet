using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// OI16 — R14's last sentence ("No command handler, aggregate or domain
/// service publishes directly") expressed as something that fails the
/// build, design.md §10. Scoped by NAMESPACE rather than by service, so
/// features 17-22 inherit it the moment they add their own relays.
/// </summary>
public sealed class FactPublisherConfinementTests
{
    private const string OutboxNamespacePattern = @"\.Infrastructure\.Outbox(\.|$)";

    [Fact]
    public void OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().DoNotResideInNamespaceMatching(OutboxNamespacePattern)
            .ShouldNot().HaveDependencyOn("Confluent.Kafka")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Only *.Infrastructure.Outbox types may depend on Confluent.Kafka. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
