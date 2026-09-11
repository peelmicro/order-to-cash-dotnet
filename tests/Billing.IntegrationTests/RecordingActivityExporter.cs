using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// design.md §12 — "spans... are asserted against OTel's IN-MEMORY
/// exporter/reader, not a live collector". Built directly on
/// <see cref="BaseExporter{T}"/>/<see cref="SimpleActivityExportProcessor"/>
/// (both in the base <c>OpenTelemetry</c> package this feature already
/// pins) rather than the separate <c>OpenTelemetry.Exporter.InMemory</c>
/// NuGet package — design.md §9.1's package table names exactly three new
/// packages, and a fourth, test-only one is not among them. Copied from
/// <c>Orders.IntegrationTests.RecordingActivityExporter</c> (review round 1,
/// D1) so <c>BillingRpcResponderTraceContinuationTests</c> can assert against
/// the REAL production <c>BillingRpcResponder</c> rather than a stand-in.
/// </summary>
internal sealed class RecordingActivityExporter : BaseExporter<Activity>
{
    private readonly ConcurrentQueue<Activity> _exported = new();

    public IReadOnlyList<Activity> Exported => _exported.ToArray();

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            _exported.Enqueue(activity);
        }

        return ExportResult.Success;
    }
}
