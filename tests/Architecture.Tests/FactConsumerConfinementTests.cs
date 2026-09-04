using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// NEW — feature <c>order_saga_orchestrator</c> (design.md §10, gate row 5,
/// approved 2026-09-04). Only <c>*.Infrastructure.Messaging.Consumers</c>
/// types may depend on <c>Confluent.Kafka</c>'s four CONSUMER type prefixes.
/// Namespace-scoped (like <see cref="FactPublisherConfinementTests"/>'s own
/// producer rule) rather than service-scoped, so features 23 and 24 inherit
/// it the moment they add their own fact-stream consumers.
///
/// <b>Same arming-time correction as <see cref="FactPublisherConfinementTests"/>'s
/// own remarks</b>: <c>NetArchTest.Rules</c> 1.3.2 matches dependency names
/// EXACTLY, and a closed generic instantiation's CLR name carries the open
/// definition's arity suffix (<c>IConsumer&lt;string, byte[]&gt;</c> is
/// <c>"Confluent.Kafka.IConsumer`2"</c>). Three of the four entries below
/// are <c>`2</c>-suffixed accordingly; <c>ConsumerConfig</c> is not generic.
/// </summary>
public sealed class FactConsumerConfinementTests
{
    private const string ConsumerNamespacePattern = @"\.Infrastructure\.Messaging\.Consumers(\.|$)";

    private static readonly string[] _consumerTypePrefixes =
    [
        "Confluent.Kafka.IConsumer`2",
        "Confluent.Kafka.Consumer`2",
        "Confluent.Kafka.ConsumerBuilder`2",
        "Confluent.Kafka.ConsumerConfig",
    ];

    [Fact]
    public void OnlyTheFactStreamConsumerAdapterMayReferenceTheKafkaConsumerClient()
    {
        foreach (var consumerTypePrefix in _consumerTypePrefixes)
        {
            var result = Types.InAssemblies(DomainAssemblies.All)
                .That().DoNotResideInNamespaceMatching(ConsumerNamespacePattern)
                .ShouldNot().HaveDependencyOn(consumerTypePrefix)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"Only *.Infrastructure.Messaging.Consumers types may depend on {consumerTypePrefix} " +
                $"(design.md §10). Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
