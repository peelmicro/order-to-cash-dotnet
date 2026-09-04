namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The delay <see cref="Infrastructure.Saga.SagaCommandDispatcher"/> waits
/// between in-line retry attempts (SO4) — a port so
/// <c>SagaCommandDispatcherTests</c> can prove the exact backoff schedule
/// (500 ms, then 1 000 ms) without a real wall-clock wait.
/// </summary>
public interface ISagaRetryDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}
