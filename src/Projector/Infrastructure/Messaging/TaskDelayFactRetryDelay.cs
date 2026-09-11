// COPY OF — src/Orders/Infrastructure/Messaging/TaskDelayFactRetryDelay.cs
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Messaging;

/// <summary>The one production <see cref="IFactRetryDelay"/> — a thin <see cref="Task.Delay(TimeSpan,CancellationToken)"/> wrapper, kept an adapter so it can be faked in tests.</summary>
public sealed class TaskDelayFactRetryDelay : IFactRetryDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
}
