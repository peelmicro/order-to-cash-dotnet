using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// The full-host suites (responders over real NATS/MS-SQL/Kafka, design.md
/// §13) join THIS collection — one MS-SQL container, one NATS broker and one
/// Kafka broker, each started once and shared by every test class here.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BillingCollection :
    ICollectionFixture<MsSqlContainerFixture>,
    ICollectionFixture<NatsContainerFixture>,
    ICollectionFixture<KafkaContainerFixture>
{
    public const string Name = "Billing";
}
