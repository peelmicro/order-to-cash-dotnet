using System.Diagnostics.Metrics;
using OrderToCash.Orders.Infrastructure.Observability;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// A thin <see cref="MeterListener"/> wrapper capturing every measurement
/// for ONE named instrument — the exact-value/exact-tag discipline design.md
/// §7 requires ("a 'greater than zero' assertion proves nothing here").
/// Listens on <see cref="OtcMetrics.MeterName"/> only, so measurements from
/// unrelated meters in the same test process never leak in.
///
/// A13 (review round 3) — <see cref="MeterListener"/> invokes its
/// measurement callbacks from WHATEVER thread recorded the measurement,
/// which under a full <c>./quality.sh</c> run (or any test enumerating
/// <see cref="Measurements"/> while a concurrently running host records on
/// the SAME process-wide meter — e.g. <c>RealInfraMetricsProvenanceTests</c>'
/// own comment) is a genuinely different thread from the one reading
/// <see cref="Measurements"/>. A plain <see cref="List{T}"/> is not safe
/// under a concurrent writer, so every write and every read takes
/// <see cref="_gate"/>, and both readers return a SNAPSHOT
/// (<c>ToArray()</c>) rather than the live list, so a caller can never
/// observe (or enumerate over) a collection still being mutated.
/// </summary>
internal sealed class MetricCapture : IDisposable
{
    /// <summary>
    /// Backlog id 74 bullet 5 (advisory A13) — the EXCLUSIVITY half, which
    /// thread safety alone does not give. The meter is PROCESS-WIDE, so a
    /// concurrently running class in this same assembly recording on the same
    /// instrument lands in this capture too, and an
    /// <c>Assert.Single(capture.Measurements)</c> then fails — or, worse,
    /// asserts against the OTHER test's measurement.
    ///
    /// <para>
    /// <c>otc_outbox_lag_ms</c> and <c>otc_dlq_depth</c> carry NO tags at all,
    /// so there is no tag to scope by. What every measurement DOES carry is
    /// the <see cref="ExecutionContext"/> of whatever code recorded it —
    /// <see cref="MeterListener"/> invokes its callback synchronously on the
    /// recording thread, and an <see cref="AsyncLocal{T}"/> set by the test
    /// before it acts flows into everything the test awaits and into nothing
    /// it does not. That is the scope: <see cref="OwnMeasurements"/> is the
    /// measurements recorded by work this test's own async flow started.
    /// </para>
    /// </summary>
    private static readonly AsyncLocal<Guid> _activeCaptureToken = new();

    private readonly Lock _gate = new();
    private readonly MeterListener _listener = new();
    private readonly List<(double Value, KeyValuePair<string, object?>[] Tags, Guid Token)> _double = [];
    private readonly List<(long Value, KeyValuePair<string, object?>[] Tags, Guid Token)> _long = [];
    private readonly Guid _token = Guid.NewGuid();
    private readonly Guid _previousToken;
    private readonly string _instrumentName;

    private MetricCapture(string instrumentName)
    {
        _instrumentName = instrumentName;
        _previousToken = _activeCaptureToken.Value;
        _activeCaptureToken.Value = _token;

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == OtcMetrics.MeterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var snapshot = tags.ToArray();
            var token = _activeCaptureToken.Value;
            lock (_gate)
            {
                _double.Add((value, snapshot, token));
            }
        });
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var snapshot = tags.ToArray();
            var token = _activeCaptureToken.Value;
            lock (_gate)
            {
                _long.Add((value, snapshot, token));
            }
        });
        _listener.Start();
    }

    public static MetricCapture ForInstrument(string instrumentName) => new(instrumentName);

    /// <summary>EVERY measurement this listener saw, this test's own and any concurrent writer's alike. Use <see cref="OwnMeasurements"/> for an exactly-one assertion.</summary>
    public IReadOnlyList<(double Value, KeyValuePair<string, object?>[] Tags)> Measurements
    {
        get
        {
            lock (_gate)
            {
                return _double.Select(m => (m.Value, m.Tags)).ToArray();
            }
        }
    }

    /// <summary>EVERY long measurement this listener saw. Use <see cref="OwnLongMeasurements"/> for an exactly-one assertion.</summary>
    public IReadOnlyList<(long Value, KeyValuePair<string, object?>[] Tags)> LongMeasurements
    {
        get
        {
            lock (_gate)
            {
                return _long.Select(m => (m.Value, m.Tags)).ToArray();
            }
        }
    }

    /// <summary>Only the measurements recorded by work this test's own async flow started — see <see cref="_activeCaptureToken"/>.</summary>
    public IReadOnlyList<(double Value, KeyValuePair<string, object?>[] Tags)> OwnMeasurements
    {
        get
        {
            lock (_gate)
            {
                return _double.Where(m => m.Token == _token).Select(m => (m.Value, m.Tags)).ToArray();
            }
        }
    }

    /// <summary>Only the long measurements recorded by work this test's own async flow started.</summary>
    public IReadOnlyList<(long Value, KeyValuePair<string, object?>[] Tags)> OwnLongMeasurements
    {
        get
        {
            lock (_gate)
            {
                return _long.Where(m => m.Token == _token).Select(m => (m.Value, m.Tags)).ToArray();
            }
        }
    }

    /// <summary>The exactly-one assertion, scoped, with a message that NAMES the instrument and every foreign measurement it ignored (backlog id 82).</summary>
    public (double Value, KeyValuePair<string, object?>[] Tags) SingleOwnMeasurement()
    {
        var own = OwnMeasurements;
        var foreign = Measurements.Count - own.Count;

        Assert.True(
            own.Count == 1,
            $"expected EXACTLY ONE '{_instrumentName}' measurement recorded by this test's own flow; saw {own.Count} "
            + $"([{string.Join(", ", own.Select(Describe))}]), plus {foreign} recorded by a concurrent writer on the same "
            + "process-wide meter, which this scope deliberately excludes.");

        return own[0];
    }

    /// <summary>The exactly-one assertion for a long instrument, scoped, with the same naming discipline.</summary>
    public (long Value, KeyValuePair<string, object?>[] Tags) SingleOwnLongMeasurement()
    {
        var own = OwnLongMeasurements;
        var foreign = LongMeasurements.Count - own.Count;

        Assert.True(
            own.Count == 1,
            $"expected EXACTLY ONE '{_instrumentName}' measurement recorded by this test's own flow; saw {own.Count} "
            + $"([{string.Join(", ", own.Select(m => DescribeLong(m)))}]), plus {foreign} recorded by a concurrent writer on the "
            + "same process-wide meter, which this scope deliberately excludes.");

        return own[0];
    }

    /// <summary>
    /// Backlog id 74 bullet 5's ARM — emits one matching-shaped measurement
    /// from a genuinely FOREIGN execution context, the way a concurrently
    /// running test class would. <see cref="ExecutionContext.SuppressFlow"/>
    /// plus a fresh <see cref="Thread"/> is what makes it foreign: without the
    /// suppression the thread would inherit this test's own
    /// <see cref="AsyncLocal{T}"/> value and the scope could not tell them
    /// apart.
    /// </summary>
    public static void RecordFromAConcurrentWriter(Action record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using (ExecutionContext.SuppressFlow())
        {
            var writer = new Thread(() => record()) { IsBackground = true };
            writer.Start();
            writer.Join();
        }
    }

    public void Dispose()
    {
        _activeCaptureToken.Value = _previousToken;
        _listener.Dispose();
    }

    private static string Describe((double Value, KeyValuePair<string, object?>[] Tags) measurement) =>
        $"{measurement.Value}{{{string.Join(",", measurement.Tags.Select(t => $"{t.Key}={t.Value}"))}}}";

    private static string DescribeLong((long Value, KeyValuePair<string, object?>[] Tags) measurement) =>
        $"{measurement.Value}{{{string.Join(",", measurement.Tags.Select(t => $"{t.Key}={t.Value}"))}}}";
}
