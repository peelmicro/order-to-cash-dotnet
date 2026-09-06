// COPY OF — src/Orders/Infrastructure/SystemClock.cs
using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure;

/// <summary>The default <see cref="IClock"/> implementation. Not <see cref="TimeProvider"/>: the only consumers are a handful of infrastructure classes, and a one-property interface needs no <c>FakeTimeProvider</c> package to fake in a test.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
