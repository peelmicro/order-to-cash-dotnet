using Microsoft.Extensions.Logging;

namespace OrderToCash.Notifications.UnitTests.TestSupport;

/// <summary>
/// A genuine <see cref="ILogger{TCategoryName}"/> — no mocking framework —
/// that records every call's level, exception and structured state as a
/// flat <see cref="IReadOnlyDictionary{TKey,TValue}"/>, the same shape
/// #7's own <c>recordingLogger()</c> test helper
/// (<c>degrading-notification-sender.spec.ts</c>) gives its tests. Used to
/// prove <see cref="OrderToCash.Notifications.Infrastructure.Notification.DegradingNotificationSender"/>'s
/// degraded-send log line carries the stable <c>Event</c> field and the
/// underlying reason, without depending on the ambient scope/Activity
/// machinery that only a real host wires up (that half is proven at the
/// integration level — see <c>NotificationDegradesOnPermanentFailureTests</c>).
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, Exception? Exception, IReadOnlyDictionary<string, object?> State, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object>> pairs)
        {
            foreach (var pair in pairs)
            {
                values[pair.Key] = pair.Value;
            }
        }

        Entries.Add(new Entry(logLevel, exception, values, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
