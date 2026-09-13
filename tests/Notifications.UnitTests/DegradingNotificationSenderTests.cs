using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Notifications.Application;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging;
using OrderToCash.Notifications.Infrastructure.Notification;
using OrderToCash.Notifications.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// Backlog id 73 — ported from #7's <c>degrading-notification-sender.spec.ts</c>,
/// three layers as its own header documents: in isolation, composed with
/// the REAL <see cref="FactRetryDispatcher"/>, composed with the REAL
/// <see cref="NotificationDispatchService"/>. The correlationId/traceId
/// half of #7's layer-1 assertions (and the whole of
/// <c>degrading-notification-sender-log-trace-id.spec.ts</c>) is relocated
/// to the integration level here — #8's ambient <c>ILogger</c>
/// scope/Activity pipeline is what supplies both, and only a real host
/// wires that pipeline up (see
/// <c>NotificationDegradesOnPermanentFailureTests</c> in
/// Notifications.IntegrationTests).
/// </summary>
public sealed class DegradingNotificationSenderTests
{
    private static readonly NotificationMessage _message = new("to@example.com", "subject", "text", "<p>html</p>");

    private static readonly SmtpCommandException _permanentError =
        new(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 mailbox unavailable");

    private static readonly SmtpCommandException _transientError =
        new(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "450 mailbox temporarily unavailable");

    // --- layer 1: in isolation ---------------------------------------

    [Fact]
    public async Task DelegatesToInner_AndNeverTouchesFallbackOnSuccess()
    {
        var inner = new FakeNotificationSender();
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);

        await sender.SendAsync(_message, CancellationToken.None);

        Assert.Single(inner.SentMessages);
        Assert.Empty(fallback.SentMessages);
    }

    /// <summary>
    /// The "must not change" half — armed by DELETING the transient rethrow
    /// (making every failure degrade silently), recorded verbatim in
    /// progress/impl_notification_send_degrades_on_permanent_failure.md.
    /// </summary>
    [Fact]
    public async Task RethrowsATransientFailure_UnchangedAndNeverCallsFallback()
    {
        var inner = new FakeNotificationSender { ThrowOnSend = _transientError };
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);

        var thrown = await Record.ExceptionAsync(() => sender.SendAsync(_message, CancellationToken.None));

        Assert.Same(_transientError, thrown);
        Assert.Empty(fallback.SentMessages);
    }

    /// <summary>
    /// The permanent half — armed by CORRUPTING one permanent status
    /// (MailboxUnavailable/550) to classify as transient (moving the >=500
    /// boundary), recorded verbatim in the same progress file. A permanent
    /// failure resolves normally, renders to the fallback, and logs one
    /// loud line carrying the stable Event field and the original
    /// exception as the reason.
    /// </summary>
    [Fact]
    public async Task APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason()
    {
        var message = _message with { MessageId = "event-73-degraded@order-to-cash" };
        var inner = new FakeNotificationSender { ThrowOnSend = _permanentError };
        var fallback = new FakeNotificationSender();
        var logger = new RecordingLogger<DegradingNotificationSender>();
        var sender = new DegradingNotificationSender(inner, fallback, logger);

        var thrown = await Record.ExceptionAsync(() => sender.SendAsync(message, CancellationToken.None));

        Assert.Null(thrown);
        Assert.Single(fallback.SentMessages);
        Assert.Equal(message, fallback.SentMessages[0]);

        Assert.Single(logger.Entries);
        var entry = logger.Entries[0];
        Assert.Same(_permanentError, entry.Exception);
        Assert.Equal("notification.send.degraded", entry.State["Event"]);
        Assert.Equal(message.To, entry.State["To"]);
        Assert.Equal(message.Subject, entry.State["Subject"]);
        // Review round 1, A3 — MessageId was logged but unguarded.
        Assert.Equal(message.MessageId, entry.State["MessageId"]);
    }

    [Fact]
    public async Task APermanentFailureIsDistinguishableInLogs_TheLogLineOnlyFiresOnDegradation()
    {
        var inner = new FakeNotificationSender();
        var fallback = new FakeNotificationSender();
        var logger = new RecordingLogger<DegradingNotificationSender>();
        var sender = new DegradingNotificationSender(inner, fallback, logger);

        await sender.SendAsync(_message, CancellationToken.None);

        Assert.Empty(logger.Entries);
    }

    // --- layer 2: composed with the REAL FactRetryDispatcher ----------

    private static FactRetryDispatcher BuildDispatcher(out List<(string SourceTopic, int Attempts)> dlqCalls, out List<int> delayCalls)
    {
        var calls = new List<(string, int)>();
        var delays = new List<int>();
        var dlq = new RecordingDlqPublisher(calls);
        var delay = new RecordingDelay(delays);
        var dispatcher = new FactRetryDispatcher(
            new FixedClock(),
            delay,
            dlq,
            Options.Create(new FactRetryOptions { MaxAttempts = 3, BackoffMs = 500 }),
            NullLogger<FactRetryDispatcher>.Instance);
        dlqCalls = calls;
        delayCalls = delays;
        return dispatcher;
    }

    private static FactStreamMessage Message() => new("otc.orders.facts.v1", 0, 0, ReadOnlyMemory<byte>.Empty);

    [Fact]
    public async Task ComposedWithTheRealFactRetryDispatcher_ATransientSendFailureRetries3xAndDeadLettersOnExhaustion()
    {
        var dispatcher = BuildDispatcher(out var dlqCalls, out var delayCalls);
        var inner = new FakeNotificationSender { ThrowOnSend = _transientError };
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);

        await dispatcher.DispatchAsync(
            "otc.orders.facts.v1",
            Message(),
            Guid.NewGuid(),
            "order.placed.v1",
            Guid.NewGuid(),
            ConsumerName.Notifications,
            ct => sender.SendAsync(_message, ct),
            CancellationToken.None);

        Assert.Equal(3, inner.SendCallCount);
        Assert.Empty(fallback.SentMessages);
        Assert.Equal([500, 1000], delayCalls);
        Assert.Single(dlqCalls);
        Assert.Equal(3, dlqCalls[0].Attempts);
    }

    /// <summary>
    /// The third arm family — replacing the permanent branch's normal
    /// return with a rethrow makes this fail: a genuinely permanent failure
    /// must reach FactRetryDispatcher as a SUCCESS (dispatch returns
    /// without throwing on attempt 1), so NO <c>.dlq</c> publication ever
    /// happens and NO retry delay is ever awaited.
    /// </summary>
    [Fact]
    public async Task ComposedWithTheRealFactRetryDispatcher_APermanentSendFailureNeverDeadLetters_SingleAttemptOnly()
    {
        var dispatcher = BuildDispatcher(out var dlqCalls, out var delayCalls);
        var inner = new FakeNotificationSender { ThrowOnSend = _permanentError };
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);

        await dispatcher.DispatchAsync(
            "otc.orders.facts.v1",
            Message(),
            Guid.NewGuid(),
            "order.placed.v1",
            Guid.NewGuid(),
            ConsumerName.Notifications,
            ct => sender.SendAsync(_message, ct),
            CancellationToken.None);

        Assert.Equal(1, inner.SendCallCount);
        Assert.Single(fallback.SentMessages);
        Assert.Empty(dlqCalls);
        Assert.Empty(delayCalls);
    }

    // --- layer 3: composed with the REAL NotificationDispatchService --

    [Fact]
    public async Task ComposedWithTheRealNotificationDispatchService_APermanentSendFailure_DispatchResolves_LedgerRowNotDeleted_FallbackReceivesTheMessage()
    {
        var inner = new FakeNotificationSender { ThrowOnSend = _permanentError };
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);
        var idempotency = new FakeNotificationIdempotency();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);
        var eventId = Guid.NewGuid();

        var outcome = await service.DispatchAsync(eventId, () => _message, CancellationToken.None);

        Assert.Equal(NotificationIdempotencyOutcome.Processed, outcome);
        Assert.Empty(idempotency.DeleteCalls);
        Assert.Single(fallback.SentMessages);
        Assert.Equal($"{eventId}@order-to-cash", fallback.SentMessages[0].MessageId);
    }

    [Fact]
    public async Task ComposedWithTheRealNotificationDispatchService_ARedeliveredEventIdAfterAPermanentDegrade_IsAGenuineDuplicate_NeverSentTwice()
    {
        var inner = new FakeNotificationSender { ThrowOnSend = _permanentError };
        var fallback = new FakeNotificationSender();
        var sender = new DegradingNotificationSender(inner, fallback, NullLogger<DegradingNotificationSender>.Instance);
        var idempotency = new FakeNotificationIdempotency();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);
        var eventId = Guid.NewGuid();

        var first = await service.DispatchAsync(eventId, () => _message, CancellationToken.None);
        var redelivered = await service.DispatchAsync(eventId, () => _message, CancellationToken.None);

        Assert.Equal(NotificationIdempotencyOutcome.Processed, first);
        Assert.Equal(NotificationIdempotencyOutcome.Duplicate, redelivered);
        Assert.Single(fallback.SentMessages);
    }

    private sealed class FixedClock : IClock
    {
        private DateTimeOffset _now = new(2026, 8, 26, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow
        {
            get
            {
                var value = _now;
                _now = _now.AddSeconds(1);
                return value;
            }
        }
    }

    private sealed class RecordingDelay(List<int> calls) : IFactRetryDelay
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            calls.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDlqPublisher(List<(string SourceTopic, int Attempts)> calls) : IDeadLetterPublisher
    {
        public Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken)
        {
            calls.Add((publication.SourceTopic, publication.Attempts));
            return Task.CompletedTask;
        }
    }
}
