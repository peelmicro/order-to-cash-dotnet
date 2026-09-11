using System.Diagnostics.Metrics;
using OrderToCash.Orders.Infrastructure.Observability;

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
    private readonly Lock _gate = new();
    private readonly MeterListener _listener = new();
    private readonly List<(double Value, KeyValuePair<string, object?>[] Tags)> _double = [];
    private readonly List<(long Value, KeyValuePair<string, object?>[] Tags)> _long = [];

    private MetricCapture(string instrumentName)
    {
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
            lock (_gate)
            {
                _double.Add((value, snapshot));
            }
        });
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var snapshot = tags.ToArray();
            lock (_gate)
            {
                _long.Add((value, snapshot));
            }
        });
        _listener.Start();
    }

    public static MetricCapture ForInstrument(string instrumentName) => new(instrumentName);

    public IReadOnlyList<(double Value, KeyValuePair<string, object?>[] Tags)> Measurements
    {
        get
        {
            lock (_gate)
            {
                return _double.ToArray();
            }
        }
    }

    public IReadOnlyList<(long Value, KeyValuePair<string, object?>[] Tags)> LongMeasurements
    {
        get
        {
            lock (_gate)
            {
                return _long.ToArray();
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}
