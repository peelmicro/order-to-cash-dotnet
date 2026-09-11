namespace OrderToCash.Projector.Application.Ports;

/// <summary>
/// The clock port <see cref="Infrastructure.Messaging.FactRetryDispatcher"/>
/// needs to stamp <c>x-first-failed-at</c>/<c>x-failed-at</c> (OR1) — this
/// service's first user of a clock port at all (matches the
/// <c>OrderToCash.Orders.Application.Ports.IClock</c> shape).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
