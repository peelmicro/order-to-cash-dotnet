using MailKit;
using MailKit.Net.Smtp;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// Backlog id 73 — pure classifier table, enumerating
/// <c>send-failure-classifier.spec.ts</c>'s own boundary table
/// (progress/impl_notification_send_degrades_on_permanent_failure.md's
/// enumeration). Every <see cref="SmtpCommandException"/> instance below is
/// constructed via MailKit's OWN public constructor
/// (<c>SmtpCommandException(SmtpErrorCode, SmtpStatusCode, string)</c>) —
/// the real type, carrying the real enum whose underlying int is confirmed
/// (by direct enumeration, not assumption) to equal the literal SMTP reply
/// code — never a hand-rolled substitute type. The boundary values
/// themselves are additionally proven against a REAL Mailpit server's real
/// wire responses in <c>SendFailureClassifierRealSmtpTests</c>
/// (Notifications.IntegrationTests); this file is the fast, exhaustive
/// table that a live server would be too slow to drive for every case.
/// </summary>
public sealed class SendFailureClassifierTests
{
    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable)] // 550 — a rejected/malformed recipient
    [InlineData(SmtpStatusCode.AuthenticationInvalidCredentials)] // 535 — the live incident #7's ledger names
    [InlineData(SmtpStatusCode.AuthenticationRequired)] // 530
    [InlineData(SmtpStatusCode.ExceededStorageAllocation)] // 552
    [InlineData((SmtpStatusCode)599)] // the top of the 5xx band
    public void Classify_AnySmtpCommandExceptionWithA5xxStatusCode_IsPermanent(SmtpStatusCode statusCode)
    {
        var error = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, statusCode, "server rejected the request");

        Assert.Equal(SendFailureClassification.Permanent, SendFailureClassifier.Classify(error));
    }

    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy)] // 450 — mailbox temporarily unavailable
    [InlineData(SmtpStatusCode.ServiceNotAvailable)] // 421
    [InlineData(SmtpStatusCode.TemporaryAuthenticationFailure)] // 454
    [InlineData((SmtpStatusCode)499)] // the top of the 4xx band
    public void Classify_AnySmtpCommandExceptionWithA4xxStatusCode_IsTransient(SmtpStatusCode statusCode)
    {
        var error = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, statusCode, "server asked to retry later");

        Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(error));
    }

    /// <summary>
    /// Corruption arm — one permanent status treated as transient proves the
    /// boundary is exercised, not merely present. Named so the arming record
    /// can point at it directly.
    /// </summary>
    [Fact]
    public void Classify_ARealMailtrapStyleQuotaExhaustedRejection_550_IsPermanent_NeverTransient()
    {
        var error = new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.MailboxUnavailable, "550 mailbox unavailable");

        Assert.Equal(SendFailureClassification.Permanent, SendFailureClassifier.Classify(error));
    }

    /// <summary>
    /// Review round 1, D1 — the fast, exhaustive counterpart of the real-wire
    /// proof in <c>SendFailureClassifierRealSmtpTests.ARealMailpitAuthRequiredRejection_530_...</c>.
    /// <see cref="ServiceNotAuthenticatedException"/> is MailKit's OWN type
    /// (constructed here via its real public constructor — the real type,
    /// never a hand-rolled substitute), raised on
    /// <c>SmtpClient.SendAsync</c> whenever the server requires
    /// authentication that was never performed. It is NOT an
    /// <see cref="SmtpCommandException"/> (its base chain is
    /// <see cref="InvalidOperationException"/> → <see cref="SystemException"/>),
    /// so before this round it fell through the classifier's default and
    /// was misclassified Transient — the exact defect D1 names.
    /// </summary>
    [Fact]
    public void Classify_AServiceNotAuthenticatedException_IsPermanent()
    {
        var error = new ServiceNotAuthenticatedException("5.7.0 Authentication required");

        Assert.Equal(SendFailureClassification.Permanent, SendFailureClassifier.Classify(error));
    }

    // --- everything that is NOT an SmtpCommandException defaults to
    // transient — #7's own default reasoning (an unknown failure is safer
    // retried than silently swallowed). Ported from
    // send-failure-classifier.spec.ts's "socket error"/"connection
    // timeout"/"connection refused"/"unrecognised error" cases, collapsed
    // into the single rule the classifier actually implements (MailKit
    // carries no analogue to nodemailer's per-code-name transientCodes
    // table — there is only ONE fallback branch to prove, and it is proven
    // against a REAL refused connection in
    // SendFailureClassifierRealSmtpTests).

    [Fact]
    public void Classify_ASocketException_IsTransient()
    {
        var error = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused);

        Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(error));
    }

    [Fact]
    public void Classify_ASmtpProtocolException_IsTransient()
    {
        var error = new SmtpProtocolException("the server closed the connection unexpectedly");

        Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(error));
    }

    [Fact]
    public void Classify_AnUnrecognisedException_DefaultsToTransient_NeverPermanent()
    {
        Assert.Equal(SendFailureClassification.Transient, SendFailureClassifier.Classify(new InvalidOperationException("something odd happened")));
    }

    /// <summary>
    /// send-failure-classifier.spec.ts's "non-Error thrown value" case has
    /// no C# equivalent — the CLR/CLS require every thrown object to derive
    /// from <see cref="Exception"/>, so <see cref="SendFailureClassifier.Classify"/>
    /// (which accepts <see cref="Exception"/>, not <see langword="object"/>)
    /// cannot even be called with one. Not applicable, not merely untested.
    /// </summary>
    [Fact]
    public void Classify_TakesExceptionNotObject_TheNonErrorThrownValueCaseHasNoEquivalent()
    {
        Assert.Equal(typeof(Exception), typeof(SendFailureClassifier).GetMethod(nameof(SendFailureClassifier.Classify))!.GetParameters()[0].ParameterType);
    }
}
