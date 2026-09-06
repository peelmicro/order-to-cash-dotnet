// COPY OF — src/Orders/Infrastructure/SystemClock.cs
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure;

/// <summary>The default <see cref="IClock"/> implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
