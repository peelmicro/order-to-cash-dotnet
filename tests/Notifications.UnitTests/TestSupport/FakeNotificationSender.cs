using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.UnitTests.TestSupport;

/// <summary>A genuine <see cref="INotificationSender"/> fake with a real call record — never a mock.</summary>
public sealed class FakeNotificationSender : INotificationSender
{
    public List<NotificationMessage> SentMessages { get; } = [];

    /// <summary>When set, <see cref="SendAsync"/> throws this instead of recording the send — the send-failure probe.</summary>
    public Exception? ThrowOnSend { get; set; }

    /// <summary>
    /// Every <see cref="SendAsync"/> call, whether it throws or not —
    /// backlog id 73's retry-count proof
    /// (<c>DegradingNotificationSenderTests</c>'s layer-2 tests) needs to
    /// count attempts against a sender that throws on EVERY call, which
    /// <see cref="SentMessages"/> alone cannot do.
    /// </summary>
    public int SendCallCount { get; private set; }

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        SendCallCount++;

        if (ThrowOnSend is { } exception)
        {
            throw exception;
        }

        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
