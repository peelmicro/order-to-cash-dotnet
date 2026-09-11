// COPY OF — tests/Orders.IntegrationTests/NatsContainerFixture.cs
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>One real NATS broker — <c>nats:2.14.5-alpine</c>, the SAME pinned tag <c>docker-compose.infra.yml</c>'s <c>nats</c> service uses, core-only.</summary>
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

    /// <summary>
    /// R60/OR6, design.md §8.3 — a REAL <c>docker pause</c>, never a faked
    /// failure. Used by <c>HealthProbesTests</c> only; every other test in
    /// a SHARED collection relies on the caller always unpausing (a
    /// <c>try</c>/<c>finally</c>) before its own test method returns.
    /// </summary>
    public Task PauseAsync() => _container!.PauseAsync();

    public Task UnpauseAsync() => _container!.UnpauseAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NatsCollection : ICollectionFixture<NatsContainerFixture>
{
    public const string Name = "GatewayNats";
}

/// <summary>Just NATS + MongoDB — the Gateway's own two readiness dependencies (design.md §8.2), for <c>HealthProbesTests</c> rather than reusing one of the larger end-to-end collections that also pull in Kafka/MS-SQL this test never needs.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GatewayHealthCollection : ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MongoContainerFixture>
{
    public const string Name = "GatewayHealth";
}
