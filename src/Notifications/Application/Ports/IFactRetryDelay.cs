// COPY OF — src/Orders/Application/Ports/IFactRetryDelay.cs
namespace OrderToCash.Notifications.Application.Ports;

/// <summary>
/// The delay <see cref="Infrastructure.Messaging.FactRetryDispatcher"/>
/// (design.md §3.2's canonical copy) waits between in-line retry attempts
/// (OR1) — the <c>ISagaRetryDelay</c> shape, so unit tests can prove the
/// exact backoff schedule (500 ms, then 1 000 ms) without a real wall-clock
/// wait.
/// </summary>
public interface IFactRetryDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}
