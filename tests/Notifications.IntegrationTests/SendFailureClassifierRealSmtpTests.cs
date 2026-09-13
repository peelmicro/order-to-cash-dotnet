using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Backlog id 73, acceptance bullet 3 — every MailKit signal
/// <see cref="SendFailureClassifier"/> relies on, proven against a REAL
/// Mailpit server's REAL wire responses (<see cref="MailpitContainerFixture"/>'s
/// own <c>--enable-chaos</c> API), never a hand-constructed exception
/// standing in for one. Drives the exact same <c>MailKit.Net.Smtp.SmtpClient</c>
/// call sequence <see cref="OrderToCash.Notifications.Infrastructure.Notification.MailKitSmtpTransport"/>
/// makes (connect, no authentication, send, disconnect) — see that class's
/// own header for why no <c>AuthenticateAsync</c> call exists to prove a
/// signal against.
/// </summary>
[Collection(NotificationsWithMailpitCollection.Name)]
public sealed class SendFailureClassifierRealSmtpTests(MailpitContainerFixture mailpit, MailpitAuthContainerFixture mailpitAuth)
{
    [Fact]
    public async Task ARealMailpitRecipientRejection_550_RaisesSmtpCommandException_AndClassifiesAsPermanent()
    {
        await mailpit.SetRecipientErrorAsync(550);
        try
        {
            var error = await Assert.ThrowsAsync<SmtpCommandException>(() => SendThroughMailpitAsync());

            Assert.Equal(SmtpStatusCode.MailboxUnavailable, error.StatusCode);
            Assert.Equal(550, (int)error.StatusCode);
            Assert.Equal(SendFailureClassification.Permanent, SendFailureClassifier.Classify(error));
        }
        finally
        {
            await mailpit.ResetChaosAsync();
        }
    }

    [Fact]
    public async Task ARealMailpitRecipientRejection_450_RaisesSmtpCommandException_AndClassifiesAsTransient()
    {
        await mailpit.SetRecipientErrorAsync(450);
        try
        {
            var error = await Assert.ThrowsAsync<SmtpCommandException>(() => SendThroughMailpitAsync());

            Assert.Equal(SmtpStatusCode.MailboxBusy, error.StatusCode);
            Assert.Equal(450, (int)error.StatusCode);
            Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(error));
        }
        finally
        {
            await mailpit.ResetChaosAsync();
        }
    }

    /// <summary>
    /// No chaos configured, and no listener at all on the probed port — a
    /// REAL refused TCP connection, never a fabricated
    /// <see cref="System.Net.Sockets.SocketException"/>. Proves the
    /// classifier's "anything that is not an SmtpCommandException defaults
    /// to transient" fallback against a genuine network failure rather than
    /// a constructed stand-in.
    /// </summary>
    [Fact]
    public async Task ARealRefusedConnection_RaisesASocketException_AndClassifiesAsTransient()
    {
        using var client = new SmtpClient();
        var thrown = await Record.ExceptionAsync(() => client.ConnectAsync(mailpit.SmtpHost, GetUnboundPort(), SecureSocketOptions.None));

        Assert.NotNull(thrown);
        Assert.IsType<System.Net.Sockets.SocketException>(thrown);
        Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(thrown!));
    }

    /// <summary>
    /// Review round 1, D1 — the signal that carries #7's second permanent
    /// case, proven against a REAL server that REQUIRES authentication
    /// (<see cref="MailpitAuthContainerFixture.AuthRequiredHost"/>/<c>Port</c>,
    /// started with <c>--smtp-auth-file</c>). Drives the exact same
    /// unauthenticated <c>connect → send → disconnect</c> sequence
    /// <see cref="OrderToCash.Notifications.Infrastructure.Notification.MailKitSmtpTransport"/>
    /// uses — no <c>AuthenticateAsync</c> call. The real server replies
    /// <c>530</c>, and MailKit raises
    /// <see cref="ServiceNotAuthenticatedException"/> — NOT an
    /// <see cref="SmtpCommandException"/>, which is exactly why this
    /// signal was previously misclassified Transient (D1).
    /// </summary>
    [Fact]
    public async Task ARealMailpitAuthRequiredRejection_530_RaisesServiceNotAuthenticatedException_AndClassifiesAsPermanent()
    {
        var thrown = await Record.ExceptionAsync(() => SendThroughAsync(mailpitAuth.AuthRequiredHost, mailpitAuth.AuthRequiredPort));

        // Asserting the EXACT type (never a base-class match) is itself
        // part of D1's proof: ServiceNotAuthenticatedException's base chain
        // is InvalidOperationException -> SystemException, so it is
        // statically never an SmtpCommandException — the compiler proves
        // that for us (CS0184 fires on a redundant runtime check of it).
        var authError = Assert.IsType<ServiceNotAuthenticatedException>(thrown);
        Assert.Equal(SendFailureClassification.Permanent, SendFailureClassifier.Classify(authError));
    }

    /// <summary>
    /// The negative control D1 also demands: the SAME unauthenticated send,
    /// against a REAL server where authentication is merely ADVERTISED
    /// (<c>--smtp-auth-accept-any</c>, no auth file) rather than REQUIRED,
    /// SUCCEEDS — proving <see cref="ServiceNotAuthenticatedException"/> is
    /// a property of the SERVER'S configuration, never of
    /// <c>MailKitSmtpTransport</c>'s own code not calling
    /// <c>AuthenticateAsync</c>.
    /// </summary>
    [Fact]
    public async Task ARealMailpitAuthAdvertisedButNotRequired_TheUnauthenticatedSendSucceeds()
    {
        var thrown = await Record.ExceptionAsync(() => SendThroughAsync(mailpitAuth.AuthAdvertisedOnlyHost, mailpitAuth.AuthAdvertisedOnlyPort));

        Assert.Null(thrown);
    }

    private Task SendThroughMailpitAsync() => SendThroughAsync(mailpit.SmtpHost, mailpit.SmtpPort);

    private static async Task SendThroughAsync(string host, int port)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse("from@order-to-cash.example"));
        mime.To.Add(MailboxAddress.Parse("to@order-to-cash.example"));
        mime.Subject = "backlog id 73 — real SMTP failure probe";
        mime.Body = new TextPart("plain") { Text = "body" };

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.None);
        try
        {
            await client.SendAsync(mime);
        }
        finally
        {
            await client.DisconnectAsync(quit: true);
        }
    }

    private static int GetUnboundPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
