using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// A test needing REAL Kafka, REAL NATS and REAL MS-SQL together joins THIS
/// collection — the saga orchestrator is the first feature that needs all
/// three at once. Follows <see cref="KafkaCollection"/>/<see cref="NatsCollection"/>'s
/// own shape: xUnit lets a collection definition implement
/// <see cref="ICollectionFixture{TFixture}"/> more than once, so all three
/// fixtures are constructed once for the whole collection (design.md §8.1).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SagaCollection :
    ICollectionFixture<KafkaContainerFixture>,
    ICollectionFixture<NatsContainerFixture>,
    ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Saga";
}
