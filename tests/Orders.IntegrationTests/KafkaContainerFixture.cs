using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
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
/// <remarks>
/// <para>
/// Backlog id 85 — the host port is DOCKER'S to choose, never this
/// fixture's. The retired shape opened a <see cref="System.Net.Sockets.TcpListener"/>
/// on port 0, read the port the OS had assigned, CLOSED the listener and
/// handed the number to <c>WithPortBinding(hostPort, containerPort)</c>:
/// nothing held the port between that check and Docker's bind, so anything
/// on the machine could take it and Docker then failed the whole container
/// at START — which kills every test in the collection at 1 ms, and did,
/// twice (57 tests in Projector.IntegrationTests, 1 in Gateway.IntegrationTests).
/// </para>
/// <para>
/// Kafka is the site that could NOT simply pass <c>true</c> and be done
/// with it: <c>KAFKA_ADVERTISED_LISTENERS</c> must carry the HOST-visible
/// address, which is exactly why a port was chosen in advance, and a
/// broker advertising a port nothing can reach is a broken broker rather
/// than a fixed race. This uses Testcontainers' documented approach for
/// Kafka instead: the container's command spins waiting for a startup
/// script, <c>WithStartupCallback</c> runs AFTER the container has started
/// (so <c>GetMappedPublicPort</c> already knows Docker's choice) and
/// BEFORE the wait strategy, and copies in a script that exports the real
/// advertised listener and then execs the image's own
/// <c>/etc/kafka/docker/run</c>. The five <c>NatsContainerFixture</c>
/// files in this repository are the in-tree control for the plain form of
/// the same idiom.
/// </para>
/// </remarks>
public sealed class KafkaContainerFixture : IAsyncLifetime
{
    private const int InternalPort = 29092;
    private const int ControllerPort = 9093;
    private const int ExternalContainerPort = 9092;

    /// <summary>The file the container's command waits for; see the remarks on this class.</summary>
    private const string StartupScriptPath = "/testcontainers_kafka_start.sh";

    private IContainer? _container;

    /// <summary>Read back from Docker after the container has started — never chosen in advance (backlog id 85).</summary>
    public string BootstrapServers { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("apache/kafka:4.3.1")
            .WithPortBinding(ExternalContainerPort, true)
            .WithCommand("/bin/sh", "-c", $"while [ ! -f {StartupScriptPath} ]; do sleep 0.1; done; exec /bin/sh {StartupScriptPath}")
            .WithStartupCallback((container, ct) => WriteAdvertisedListenerScriptAsync(container, ct))
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", $"PLAINTEXT://:{InternalPort},CONTROLLER://:{ControllerPort},EXTERNAL://:{ExternalContainerPort}")
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

        BootstrapServers = $"localhost:{_container.GetMappedPublicPort(ExternalContainerPort)}";

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

    /// <summary>
    /// Runs after the container has started and before the wait strategy —
    /// the only moment at which Docker's chosen host port is known AND the
    /// broker has not yet read its configuration (backlog id 85).
    /// </summary>
    private static Task WriteAdvertisedListenerScriptAsync(IContainer container, CancellationToken cancellationToken)
    {
        var advertised = $"PLAINTEXT://localhost:{InternalPort},EXTERNAL://localhost:{container.GetMappedPublicPort(ExternalContainerPort)}";
        var script = $"#!/bin/sh\nexport KAFKA_ADVERTISED_LISTENERS='{advertised}'\nexec /etc/kafka/docker/run\n";
        return container.CopyAsync(Encoding.UTF8.GetBytes(script), StartupScriptPath, fileMode: Unix.FileMode755, ct: cancellationToken);
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
