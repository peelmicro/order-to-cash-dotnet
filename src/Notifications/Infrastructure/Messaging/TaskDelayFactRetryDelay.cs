// COPY OF — src/Orders/Infrastructure/Messaging/TaskDelayFactRetryDelay.cs
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Messaging;

/// <summary>The one production <see cref="IFactRetryDelay"/> — a thin <see cref="Task.Delay(TimeSpan,CancellationToken)"/> wrapper, kept an adapter so it can be faked in tests.</summary>
public sealed class TaskDelayFactRetryDelay : IFactRetryDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
}
