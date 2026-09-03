using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>A settable <see cref="IClock"/> — design.md §4.6's "testable without a FakeTimeProvider package".</summary>
internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    /// <summary>
    /// <c>DateTimeOffset.UtcNow</c> truncated to millisecond precision — the
    /// precision <c>datetime2(3)</c> columns and the wire actually carry
    /// (design.md §8.2). A test that seeds a clock with sub-millisecond
    /// ticks and then asserts a stored/round-tripped instant against it is
    /// asserting a precision the storage layer was never going to keep.
    /// </summary>
    public static DateTimeOffset UtcNowToTheMillisecond()
    {
        var now = DateTimeOffset.UtcNow;
        var truncatedTicks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond);
        return new DateTimeOffset(truncatedTicks, TimeSpan.Zero);
    }
}
