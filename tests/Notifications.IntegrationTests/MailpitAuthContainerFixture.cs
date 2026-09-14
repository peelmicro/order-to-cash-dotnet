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
/// <remarks>
/// Backlog id 85 — all four host ports are Docker's to choose
/// (<c>WithPortBinding(containerPort, true)</c> then
/// <c>GetMappedPublicPort</c>), never this fixture's. Note that the two
/// containers now bind the SAME container-side ports: that is not a clash,
/// because each gets its own host port from Docker, which is precisely the
/// property the retired self-assigned shape had to arrange by hand.
/// </remarks>
public sealed class MailpitAuthContainerFixture : IAsyncLifetime
{
    private const int SmtpContainerPort = 1025;
    private const int HttpContainerPort = 8025;
    private const string AuthFileContent = "testuser:testpass\n";

    private IContainer? _authRequired;
    private IContainer? _authAdvertisedOnly;

    public string AuthRequiredHost => "localhost";

    /// <summary>Read back from Docker after the container has started — never chosen in advance (backlog id 85).</summary>
    public int AuthRequiredPort { get; private set; }

    public string AuthAdvertisedOnlyHost => "localhost";

    /// <summary>Read back from Docker after the container has started — never chosen in advance (backlog id 85).</summary>
    public int AuthAdvertisedOnlyPort { get; private set; }

    public async Task InitializeAsync()
    {
        _authRequired = new ContainerBuilder("axllent/mailpit:v1.27.5")
            .WithPortBinding(SmtpContainerPort, true)
            .WithPortBinding(HttpContainerPort, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(AuthFileContent), "/authfile")
            .WithCommand("--smtp-auth-file=/authfile", "--smtp-auth-allow-insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("accessible via"))
            .Build();

        _authAdvertisedOnly = new ContainerBuilder("axllent/mailpit:v1.27.5")
            .WithPortBinding(SmtpContainerPort, true)
            .WithPortBinding(HttpContainerPort, true)
            .WithCommand("--smtp-auth-accept-any", "--smtp-auth-allow-insecure")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("accessible via"))
            .Build();

        await _authRequired.StartAsync();
        await _authAdvertisedOnly.StartAsync();

        AuthRequiredPort = _authRequired.GetMappedPublicPort(SmtpContainerPort);
        AuthAdvertisedOnlyPort = _authAdvertisedOnly.GetMappedPublicPort(SmtpContainerPort);
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
}
