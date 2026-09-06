using System.Net;
using System.Net.Sockets;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// One real Kafka broker — <c>apache/kafka:4.3.1</c>, the SAME pinned tag
/// <c>docker-compose.infra.yml</c>'s <c>kafka</c> service and
/// Orders.IntegrationTests' own <c>KafkaContainerFixture</c> use. Copied
/// (not shared — CLAUDE.md's "per-service copies" precedent) rather than
/// referenced across test projects, the same way every other per-service
/// fixture in this repository is.
/// </summary>
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

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = NotificationFactTopics.OrdersFacts, NumPartitions = 6, ReplicationFactor = 1 },
            new TopicSpecification { Name = NotificationFactTopics.FulfillmentFacts, NumPartitions = 6, ReplicationFactor = 1 },
            new TopicSpecification { Name = NotificationFactTopics.BillingFacts, NumPartitions = 6, ReplicationFactor = 1 },
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

/// <summary>A test needing BOTH real Kafka and real MS-SQL joins THIS collection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NotificationsCollection : ICollectionFixture<KafkaContainerFixture>, ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Notifications";
}
