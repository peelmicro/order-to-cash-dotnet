using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>The one production <see cref="IFactRetryDelay"/> — a thin <see cref="Task.Delay(TimeSpan,CancellationToken)"/> wrapper, kept an adapter so it can be faked in <c>FactRetryDispatcherTests</c> (the <c>TaskDelaySagaRetryDelay</c> shape).</summary>
public sealed class TaskDelayFactRetryDelay : IFactRetryDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
}
