using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>The one production <see cref="ISagaRetryDelay"/> — a thin <see cref="Task.Delay(TimeSpan,CancellationToken)"/> wrapper, kept an adapter so it can be faked in <c>SagaCommandDispatcherTests</c>.</summary>
public sealed class TaskDelaySagaRetryDelay : ISagaRetryDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
}
