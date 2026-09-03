using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// One real NATS broker — <c>nats:2.14.5-alpine</c>, the SAME pinned tag
/// <c>docker-compose.infra.yml</c>'s <c>nats</c> service uses, core-only (no
/// JetStream — <c>asyncapi.yaml</c> <c>servers.rpcTransport</c>: "NATS core
/// request-reply. No durability, no replay, no stream"). No
/// <c>Testcontainers.Nats</c> package exists for the RPC transport this
/// repository targets, so — following the identical, already-reviewed
/// precedent this project set for Kafka (<see cref="KafkaContainerFixture"/>'s
/// own remarks) — the generic <see cref="ContainerBuilder"/> drives it
/// directly rather than adding an unused package.
/// </summary>
public sealed class NatsContainerFixture : IAsyncLifetime
{
    private const int ClientPort = 4222;

    private IContainer? _container;

    public string Url { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("nats:2.14.5-alpine")
            .WithPortBinding(ClientPort, true)
            .WithCommand("-p", ClientPort.ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();

        await _container.StartAsync();

        Url = $"nats://{_container.Hostname}:{_container.GetMappedPublicPort(ClientPort)}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>A test needing both real NATS and real MS-SQL joins this collection — the shape <see cref="KafkaCollection"/> already established for Kafka+MsSql.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NatsCollection : ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Nats";
}
