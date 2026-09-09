using OrderToCash.Gateway.Application.Ports;

namespace OrderToCash.Gateway.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
