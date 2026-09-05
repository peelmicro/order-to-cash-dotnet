// COPY OF — src/Orders/Infrastructure/SystemClock.cs
using OrderToCash.Fulfillment.Application.Ports;

namespace OrderToCash.Fulfillment.Infrastructure;

/// <summary>The default <see cref="IClock"/> implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
