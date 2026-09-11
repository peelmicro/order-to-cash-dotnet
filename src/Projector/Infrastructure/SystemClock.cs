using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure;

/// <summary>The default <see cref="IClock"/> implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
