// COPY OF — tests/Orders.IntegrationTests/NatsContainerFixture.cs
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// One real NATS broker — <c>nats:2.14.5-alpine</c>, the SAME pinned tag
/// <c>docker-compose.infra.yml</c>'s <c>nats</c> service uses, core-only.
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
