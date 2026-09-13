using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Backlog id 73 — a REAL Mailpit server (<c>axllent/mailpit:v1.27.5</c>,
/// the SAME pinned tag <c>docker-compose.infra.yml</c>'s <c>mailpit</c>
/// service uses), started with <c>--enable-chaos</c>: Mailpit's own chaos
/// API (<c>PUT /api/v1/chaos</c>) makes the server reject the SMTP
/// <c>MAIL FROM</c>/<c>RCPT TO</c> commands with a caller-chosen, genuine
/// three-digit SMTP reply code — the mechanism this feature's acceptance
/// bullet 3 requires ("proven against a real SMTP failure from a real
/// server ... never a hand-constructed exception"). Confirmed directly
/// (this fixture's own probe, recorded in
/// progress/impl_notification_send_degrades_on_permanent_failure.md): a
/// real <c>MailKit.Net.Smtp.SmtpClient</c> talking to this server with
/// <c>Recipient.ErrorCode = 550</c> raises the real
/// <c>MailKit.Net.Smtp.SmtpCommandException</c> with
/// <c>StatusCode = SmtpStatusCode.MailboxUnavailable</c> (== 550) — the
/// exact signal <see cref="OrderToCash.Notifications.Infrastructure.Notification.SendFailureClassifier"/>
/// reads.
/// </summary>
public sealed class MailpitContainerFixture : IAsyncLifetime, IDisposable
{
    private const int SmtpContainerPort = 1025;
    private const int HttpContainerPort = 8025;

    private readonly int _smtpHostPort = GetFreeTcpPort();
    private readonly int _httpHostPort = GetFreeTcpPort();
    private IContainer? _container;
    private HttpClient? _http;

    public string SmtpHost => "localhost";

    public int SmtpPort => _smtpHostPort;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("axllent/mailpit:v1.27.5")
            .WithPortBinding(_smtpHostPort, SmtpContainerPort)
            .WithPortBinding(_httpHostPort, HttpContainerPort)
            .WithCommand("--enable-chaos")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("accessible via"))
            .Build();

        await _container.StartAsync();

        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{_httpHostPort}") };

        await ResetChaosAsync();
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public void Dispose() => _http?.Dispose();

    /// <summary>
    /// Configures the server to reject every <c>RCPT TO</c> with
    /// <paramref name="errorCode"/> (a genuine SMTP reply code, e.g. 550 or
    /// 450) at the given probability (0-100). <see cref="ResetChaosAsync"/>
    /// disables every trigger again.
    /// </summary>
    public async Task SetRecipientErrorAsync(int errorCode, int probability = 100)
    {
        var response = await _http!.PutAsJsonAsync("/api/v1/chaos", new
        {
            Sender = new { ErrorCode = 451, Probability = 0 },
            Recipient = new { ErrorCode = errorCode, Probability = probability },
            Authentication = new { ErrorCode = 535, Probability = 0 },
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetChaosAsync()
    {
        var response = await _http!.PutAsJsonAsync("/api/v1/chaos", new
        {
            Sender = new { ErrorCode = 451, Probability = 0 },
            Recipient = new { ErrorCode = 550, Probability = 0 },
            Authentication = new { ErrorCode = 535, Probability = 0 },
        });
        response.EnsureSuccessStatusCode();
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

/// <summary>A test needing real Kafka, real MS-SQL AND a real SMTP server joins THIS collection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NotificationsWithMailpitCollection :
    ICollectionFixture<KafkaContainerFixture>,
    ICollectionFixture<MsSqlContainerFixture>,
    ICollectionFixture<MailpitContainerFixture>,
    ICollectionFixture<MailpitAuthContainerFixture>
{
    public const string Name = "NotificationsWithMailpit";
}
