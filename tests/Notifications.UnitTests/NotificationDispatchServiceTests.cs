using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Notifications.Application;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// NS8 — idempotent by eventId, both halves (a countable claim: at most one
/// send AND at most one ledger record per eventId), plus N6/N13's
/// insert-first / delete-on-throw / never-mask-the-original-error shape.
/// </summary>
public sealed class NotificationDispatchServiceTests
{
    private static readonly NotificationMessage _message = new("to@example.com", "subject", "text", "html");

    [Fact]
    public async Task DispatchAsync_FirstDelivery_RecordsThenSendsAndAttachesTheMessageId()
    {
        var idempotency = new FakeNotificationIdempotency();
        var sender = new FakeNotificationSender();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);
        var eventId = Guid.NewGuid();

        var outcome = await service.DispatchAsync(eventId, () => _message, CancellationToken.None);

        Assert.Equal(NotificationIdempotencyOutcome.Processed, outcome);
        Assert.Single(sender.SentMessages);
        Assert.Equal($"{eventId}@order-to-cash", sender.SentMessages[0].MessageId);
        Assert.Equal([eventId], idempotency.RecordCalls);
        Assert.Empty(idempotency.DeleteCalls);
    }

    /// <summary>
    /// The absence half of NS8 — a redelivered eventId must send NO second
    /// email, and <paramref name="buildMessage"/> itself must never even run
    /// (proving the redelivery costs nothing beyond the ledger's own
    /// duplicate-key check, not merely "the send happened not to be
    /// observed").
    /// </summary>
    [Fact]
    public async Task DispatchAsync_RedeliveredEventId_SendsNoSecondEmailAndNeverBuildsTheMessage()
    {
        var idempotency = new FakeNotificationIdempotency();
        var sender = new FakeNotificationSender();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);
        var eventId = Guid.NewGuid();

        var buildCount = 0;
        NotificationMessage Build()
        {
            buildCount++;
            return _message;
        }

        var first = await service.DispatchAsync(eventId, Build, CancellationToken.None);
        var second = await service.DispatchAsync(eventId, Build, CancellationToken.None);

        Assert.Equal(NotificationIdempotencyOutcome.Processed, first);
        Assert.Equal(NotificationIdempotencyOutcome.Duplicate, second);
        Assert.Equal(1, buildCount);
        Assert.Single(sender.SentMessages);
        Assert.Equal([eventId, eventId], idempotency.RecordCalls);
    }

    [Fact]
    public async Task DispatchAsync_ADistinctEventId_IsNotTreatedAsADuplicate()
    {
        var idempotency = new FakeNotificationIdempotency();
        var sender = new FakeNotificationSender();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);

        await service.DispatchAsync(Guid.NewGuid(), () => _message, CancellationToken.None);
        await service.DispatchAsync(Guid.NewGuid(), () => _message, CancellationToken.None);

        Assert.Equal(2, sender.SentMessages.Count);
    }

    /// <summary>N6 — a failed send deletes the just-recorded ledger row and rethrows, so redelivery gets a fresh attempt rather than being permanently swallowed as a duplicate.</summary>
    [Fact]
    public async Task DispatchAsync_WhenSendThrows_DeletesTheJustRecordedRowAndRethrows()
    {
        var idempotency = new FakeNotificationIdempotency();
        var sender = new FakeNotificationSender { ThrowOnSend = new InvalidOperationException("SMTP down") };
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);
        var eventId = Guid.NewGuid();

        var thrown = await Record.ExceptionAsync(() => service.DispatchAsync(eventId, () => _message, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal("SMTP down", thrown!.Message);
        Assert.Equal([eventId], idempotency.DeleteCalls);
    }

    /// <summary>N13 — a failing compensation must never mask the original send error; the original exception is always what propagates.</summary>
    [Fact]
    public async Task DispatchAsync_WhenBothSendAndCompensationThrow_RethrowsTheOriginalSendError()
    {
        var idempotency = new FakeNotificationIdempotency { ThrowOnDelete = new InvalidOperationException("database unreachable") };
        var sender = new FakeNotificationSender { ThrowOnSend = new InvalidOperationException("SMTP down") };
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);

        var thrown = await Record.ExceptionAsync(() => service.DispatchAsync(Guid.NewGuid(), () => _message, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal("SMTP down", thrown!.Message);
    }

    /// <summary>On success, the row must never be deleted — the compensation is send-failure-only.</summary>
    [Fact]
    public async Task DispatchAsync_OnASuccessfulSend_NeverCallsDeleteRecordAsync()
    {
        var idempotency = new FakeNotificationIdempotency();
        var sender = new FakeNotificationSender();
        var service = new NotificationDispatchService(idempotency, sender, NullLogger<NotificationDispatchService>.Instance);

        await service.DispatchAsync(Guid.NewGuid(), () => _message, CancellationToken.None);

        Assert.Empty(idempotency.DeleteCalls);
    }
}
