using System.Net;
using System.Net.Sockets;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// One real Kafka broker — <c>apache/kafka:4.3.1</c>, the SAME pinned tag
/// as <c>docker-compose.infra.yml</c>'s <c>kafka</c> service (design.md
/// §9.3), the first real broker in this repository's test suite. Shared by
/// every test class in <see cref="KafkaCollection"/> so the image's KRaft
/// bootstrap cost is paid once per test run.
/// </summary>
/// <remarks>
/// <c>Testcontainers.Kafka</c>'s <c>KafkaBuilder</c> targets the Confluent
/// image family and could not drive this image — probed directly:
/// <c>KAFKA_ADVERTISED_LISTENERS</c> never reached the broker in a form it
/// accepted, and the container exited with <c>ConfigException:
/// 'advertised.listeners' values must not be empty</c>. The generic
/// <see cref="ContainerBuilder"/> below drives it correctly, with the same
/// KRaft environment shape the compose service uses. Per design.md §9.3,
/// the <c>Testcontainers.Kafka</c> package is therefore deliberately NOT
/// referenced anywhere in this solution — #7 installed it, never imported
/// it, and its reviewer recorded that as a defect.
/// </remarks>
public sealed class KafkaContainerFixture : IAsyncLifetime
{
    private const int InternalPort = 29092;
    private const int ControllerPort = 9093;
    private const int ExternalContainerPort = 9092;

    private readonly int _hostExternalPort = GetFreeTcpPort();
    private IContainer? _container;

    public string BootstrapServers => $"localhost:{_hostExternalPort}";

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("apache/kafka:4.3.1")
            .WithPortBinding(_hostExternalPort, ExternalContainerPort)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", $"PLAINTEXT://:{InternalPort},CONTROLLER://:{ControllerPort},EXTERNAL://:{ExternalContainerPort}")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"PLAINTEXT://localhost:{InternalPort},EXTERNAL://{BootstrapServers}")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", $"1@localhost:{ControllerPort}")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "false")
            .WithEnvironment("CLUSTER_ID", "MkU3OEVBNTcwNTJENDM2Qk")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();

        await _container.StartAsync();

        // infra/kafka/create-topics.sh's own numbers — 6 partitions, RF 1 —
        // never auto-creation (the broker is configured with
        // KAFKA_AUTO_CREATE_TOPICS_ENABLE=false, and auto-creation would in
        // any case yield one partition and make R15's partitioning test
        // vacuous). Extended by order_saga_orchestrator (design.md §8.1) to
        // create all THREE fact topics — the saga consumes
        // otc.fulfillment.facts.v1 and otc.billing.facts.v1 too, not just
        // its own producer's topic.
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = OrdersFactTopic.Name, NumPartitions = 6, ReplicationFactor = 1 },
            new TopicSpecification { Name = SagaFactTopics.FulfillmentFacts, NumPartitions = 6, ReplicationFactor = 1 },
            new TopicSpecification { Name = SagaFactTopics.BillingFacts, NumPartitions = 6, ReplicationFactor = 1 },
        ]);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// A test needing BOTH real Kafka and real MS-SQL (every relay test) joins
/// THIS collection rather than <see cref="MsSqlCollection"/> — xUnit lets a
/// collection definition implement <see cref="ICollectionFixture{T}"/> more
/// than once, so both fixtures are constructed once for the whole
/// collection and injected side by side. This spins up a SEPARATE MS-SQL
/// container from the one <see cref="MsSqlCollection"/> uses (extra ~20-30s
/// paid once per test run, not per test), which is the price of keeping the
/// two collections independent rather than coupling every schema test in
/// this project to a Kafka broker it never needs.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaCollection : ICollectionFixture<KafkaContainerFixture>, ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Kafka";
}
