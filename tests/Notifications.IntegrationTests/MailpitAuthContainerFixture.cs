using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Review round 1, D1 (backlog id 73) — two REAL Mailpit servers
/// (<c>axllent/mailpit:v1.27.5</c>, the same pinned tag every other fixture
/// in this project uses), proving both directions of the auth-required
/// signal on a real wire, never a hand-constructed exception:
///
///   - <see cref="AuthRequiredHost"/>/<see cref="AuthRequiredPort"/> — started
///     with <c>--smtp-auth-file</c>, so authentication is REQUIRED. A real
///     <c>MailKit.Net.Smtp.SmtpClient</c> that connects and sends WITHOUT
///     calling <c>AuthenticateAsync</c> (exactly
///     <see cref="OrderToCash.Notifications.Infrastructure.Notification.MailKitSmtpTransport"/>'s
///     own sequence) receives a genuine <c>530</c> reply and MailKit raises
///     <c>MailKit.ServiceNotAuthenticatedException</c> — confirmed directly
///     (scratch probe, before this fixture existed): base chain
///     <c>ServiceNotAuthenticatedException → InvalidOperationException →
///     SystemException</c>, message <c>"5.7.0 Authentication required"</c>.
///   - <see cref="AuthAdvertisedOnlyHost"/>/<see cref="AuthAdvertisedOnlyPort"/> —
///     started with <c>--smtp-auth-accept-any</c> and NO auth file, so AUTH
///     is advertised in the server's capabilities but never enforced. The
///     SAME unauthenticated send SUCCEEDS — the negative control the
///     review's D1 finding names: not authenticating is a PRECONDITION of
///     the 530, never a defence against it, and the exception is a property
///     of the SERVER'S configuration, not of this transport's code.
/// </summary>
public sealed class MailpitAuthContainerFixture : IAsyncLifetime
{
    private const int SmtpContainerPort = 1025;
    private const int HttpContainerPort = 8025;
    private const string AuthFileContent = "testuser:testpass\n";

    private readonly int _authRequiredSmtpPort = GetFreeTcpPort();
    private readonly int _authRequiredHttpPort = GetFreeTcpPort();
    private readonly int _authAdvertisedOnlySmtpPort = GetFreeTcpPort();
    private readonly int _authAdvertisedOnlyHttpPort = GetFreeTcpPort();

    private IContainer? _authRequired;
    private IContainer? _authAdvertisedOnly;

    public string AuthRequiredHost => "localhost";

    public int AuthRequiredPort => _authRequiredSmtpPort;

    public string AuthAdvertisedOnlyHost => "localhost";

    public int AuthAdvertisedOnlyPort => _authAdvertisedOnlySmtpPort;

    public async Task InitializeAsync()
    {
        _authRequired = new ContainerBuilder("axllent/mailpit:v1.27.5")
            .WithPortBinding(_authRequiredSmtpPort, SmtpContainerPort)
            .WithPortBinding(_authRequiredHttpPort, HttpContainerPort)
            .WithResourceMapping(Encoding.UTF8.GetBytes(AuthFileContent), "/authfile")
            .WithCommand("--smtp-auth-file=/authfile", "--smtp-auth-allow-insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("accessible via"))
            .Build();

        _authAdvertisedOnly = new ContainerBuilder("axllent/mailpit:v1.27.5")
            .WithPortBinding(_authAdvertisedOnlySmtpPort, SmtpContainerPort)
            .WithPortBinding(_authAdvertisedOnlyHttpPort, HttpContainerPort)
            .WithCommand("--smtp-auth-accept-any", "--smtp-auth-allow-insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("accessible via"))
            .Build();

        await _authRequired.StartAsync();
        await _authAdvertisedOnly.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_authRequired is not null)
        {
            await _authRequired.DisposeAsync();
        }

        if (_authAdvertisedOnly is not null)
        {
            await _authAdvertisedOnly.DisposeAsync();
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
