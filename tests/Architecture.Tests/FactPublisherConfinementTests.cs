using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// OI16 — R14's last sentence ("No command handler, aggregate or domain
/// service publishes directly") expressed as something that fails the
/// build, design.md §10. Scoped by NAMESPACE rather than by service, so
/// features 17-22 inherit it the moment they add their own relays.
/// </summary>
/// <remarks>
/// AMENDED by <c>order_saga_orchestrator</c> (design.md §10, gate row 5,
/// approved 2026-09-04). The rule as written forbade ANY type outside
/// <c>*.Infrastructure.Outbox</c> from depending on <c>Confluent.Kafka</c> at
/// all — but that guard is broader than what R14's own sentence protects.
/// R14 forbids a command handler, aggregate or domain service from
/// PUBLISHING directly; it says nothing about CONSUMING. The first
/// fact-stream consumer this repository builds
/// (<c>KafkaFactStreamSubscriber</c>) therefore cannot compile green under
/// the old wording, even though it never publishes anything. Narrowed here
/// to the four PRODUCER type prefixes only — <c>Confluent.Kafka.IProducer</c>,
/// <c>Producer</c>, <c>ProducerBuilder</c>, <c>ProducerConfig</c> — which is
/// what R14 actually names. The CONSUMER surface is confined separately by
/// <see cref="FactConsumerConfinementTests"/>, which is new. Net effect: the
/// repository gains a rule it did not have (consumer confinement) and keeps
/// the one it did (producer confinement), each stated in terms of what it
/// protects — not a relaxation, a repair of an over-written guard.
///
/// <b>Arming-time correction (tasks.md J3), recorded rather than silently
/// applied.</b> design.md §10 asserts "NetArchTest matches dependency names
/// by prefix" — empirically false for a GENERIC type. <c>NetArchTest.Rules</c>
/// 1.3.2's <c>HaveDependencyOn</c> does an EXACT match against the
/// dependency's CLR metadata name, and for a closed generic instantiation
/// that name carries the open definition's arity suffix — e.g.
/// <c>ProducerBuilder&lt;string, byte[]&gt;</c> resolves to
/// <c>"Confluent.Kafka.ProducerBuilder`2"</c>, not a value the plain string
/// <c>"Confluent.Kafka.ProducerBuilder"</c> matches. Verified by direct
/// probe: a <c>ProducerBuilder&lt;string, byte[]&gt;</c> reference added
/// under <c>Application/</c> left this rule GREEN with the un-suffixed
/// string (arming would not have fired) and RED once the arity suffix was
/// added (see progress/impl_order_saga_orchestrator.md). Three of the four
/// entries below are therefore <c>`2</c>-suffixed; <c>ProducerConfig</c> is
/// not generic and needs none. This is the "confinement rule that passes
/// because nothing in the assembly happens to use the type is a rule that
/// does not guard" failure tasks.md J3 names — caught by arming, not
/// inspection, and fixed here rather than reported as a design defect to
/// leave unguarded, because the fix changes only HOW the four names already
/// approved at the gate are spelled for an exact-match library, not WHAT the
/// rule protects.
/// </remarks>
public sealed class FactPublisherConfinementTests
{
    private const string OutboxNamespacePattern = @"\.Infrastructure\.Outbox(\.|$)";

    private static readonly string[] _producerTypePrefixes =
    [
        "Confluent.Kafka.IProducer`2",
        "Confluent.Kafka.Producer`2",
        "Confluent.Kafka.ProducerBuilder`2",
        "Confluent.Kafka.ProducerConfig",
    ];

    [Fact]
    public void OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient()
    {
        foreach (var producerTypePrefix in _producerTypePrefixes)
        {
            var result = Types.InAssemblies(DomainAssemblies.All)
                .That().DoNotResideInNamespaceMatching(OutboxNamespacePattern)
                .ShouldNot().HaveDependencyOn(producerTypePrefix)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"Only *.Infrastructure.Outbox types may depend on {producerTypePrefix} — R14's \"no command handler, " +
                $"aggregate or domain service publishes directly\" (design.md §10). Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
