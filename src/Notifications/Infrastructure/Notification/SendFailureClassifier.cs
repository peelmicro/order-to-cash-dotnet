using MailKit;
using MailKit.Net.Smtp;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// Whether a <see cref="INotificationSender.SendAsync"/> failure is worth
/// retrying (<see cref="Transient"/>, the existing <c>FactRetryDispatcher</c>
/// retry-then-dead-letter path handles it exactly as before) or is not
/// (<see cref="Permanent"/>, <see cref="DegradingNotificationSender"/>
/// degrades to the console fallback and acknowledges the fact).
/// </summary>
public enum SendFailureClassification
{
    Transient,
    Permanent,
}

/// <summary>
/// Backlog id 73 — ported from #7's
/// <c>apps/notifications/src/infrastructure/notification/send-failure-classifier.ts</c>,
/// rebuilt on MailKit's own signals rather than transliterated from
/// nodemailer's <c>responseCode</c>/<c>code</c> fields, which MailKit does
/// not have (the ported-idiom ledger row in
/// <c>progress/impl_notification_send_degrades_on_permanent_failure.md</c>
/// names both substitutions).
///
/// Two real, documented MailKit signals are checked, both PERMANENT:
///
///   1. <see cref="SmtpCommandException.StatusCode"/> — a <see cref="SmtpStatusCode"/>
///      enum whose underlying <see langword="int"/> IS the real three-digit
///      SMTP reply code (confirmed by enumerating every <see cref="SmtpStatusCode"/>
///      member: <c>MailboxUnavailable = 550</c>, <c>MailboxBusy = 450</c>,
///      <c>AuthenticationRequired = 530</c>, etc. — never inferred). This is
///      RFC 5321 §4.2.1's OWN classification, exactly the rule #7 applied to
///      nodemailer's <c>responseCode</c>: a <c>5yz</c> reply is a Permanent
///      Negative Completion, a <c>4yz</c> reply is Transient. Proven against
///      a REAL Mailpit server with its <c>--enable-chaos</c> API forcing a
///      genuine 550 (permanent) and a genuine 450 (transient) SMTP
///      response — <c>SendFailureClassifierRealSmtpTests</c>.
///   2. <see cref="ServiceNotAuthenticatedException"/> — MailKit's OWN type
///      for exactly one condition, per its XML docs on every
///      <c>SmtpClient.Send</c>/<c>SendAsync</c> overload: "Authentication is
///      required before sending a message." This is #7's second permanent
///      case (nodemailer's <c>code === 'EAUTH'</c> with no response code) —
///      and, corrected after review round 1's D1, it is NOT structurally
///      unreachable from this transport. <see cref="MailKitSmtpTransport"/>
///      never calling <c>AuthenticateAsync</c> is the PRECONDITION of this
///      exception, not a defence against it: a real server configured to
///      REQUIRE authentication (Mailpit started with <c>--smtp-auth-file</c>)
///      raises this exact type on <c>SendAsync</c>, proven live —
///      <c>SendFailureClassifierRealSmtpTests.ARealMailpitAuthRequiredRejection_530_...</c>.
///      The negative control matters too and is proven alongside it: a
///      server that merely ADVERTISES authentication without requiring it
///      (Mailpit started with <c>--smtp-auth-accept-any</c>, no
///      <c>--smtp-auth-file</c>) lets the unauthenticated send SUCCEED —
///      <c>ARealMailpitAuthAdvertisedButNotRequired_TheSendSucceeds</c> — so
///      the exception is a property of the SERVER'S configuration, never of
///      this transport's own code.
///
/// Anything that is NEITHER of the above — a socket failure before any SMTP
/// response was ever read (<see cref="System.Net.Sockets.SocketException"/>),
/// a protocol-level error (<see cref="SmtpProtocolException"/>), a
/// <see cref="SmtpCommandException"/> carrying a 4xx status, or anything
/// unrecognised — defaults to <see cref="SendFailureClassification.Transient"/>,
/// exactly #7's own default reasoning: dead-lettering is visible (a row in a
/// DLQ topic, alertable), silently swallowing an unknown failure as
/// permanent is not.
/// </summary>
public static class SendFailureClassifier
{
    public static SendFailureClassification Classify(Exception error)
    {
        if (error is SmtpCommandException { StatusCode: var statusCode } && (int)statusCode is >= 500 and < 600)
        {
            return SendFailureClassification.Permanent;
        }

        if (error is ServiceNotAuthenticatedException)
        {
            return SendFailureClassification.Permanent;
        }

        return SendFailureClassification.Transient;
    }
}
