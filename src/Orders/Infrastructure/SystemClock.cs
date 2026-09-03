using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>The default <see cref="IClock"/> implementation — design.md §4.6. Not <see cref="TimeProvider"/>: the only consumers are three infrastructure classes, and a one-property interface needs no <c>FakeTimeProvider</c> package to fake in a test.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
