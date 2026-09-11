using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// design.md §12 — "spans... are asserted against OTel's IN-MEMORY
/// exporter/reader, not a live collector". Built directly on
/// <see cref="BaseExporter{T}"/>/<see cref="SimpleActivityExportProcessor"/>
/// (both in the base <c>OpenTelemetry</c> package this feature already
/// pins) rather than the separate <c>OpenTelemetry.Exporter.InMemory</c>
/// NuGet package. Byte-parity sibling of the Orders.IntegrationTests copy
/// of the same name.
///
/// D10 (review round 3) — this copy's own distinguishing use is being
/// wired into the GATEWAY HOST'S OWN <c>TracerProviderBuilder</c> via
/// <c>services.ConfigureOpenTelemetryTracerProvider(...)</c> in
/// <see cref="GatewayTestHost.StartAsync"/>'s <c>overrideServices</c>
/// callback — the SAME builder <c>AddGatewayTelemetry</c> configures with
/// <c>.AddAspNetCoreInstrumentation()</c> — never a second, test-built
/// <c>TracerProvider</c> the way <c>TraceContextPropagationTests</c>'
/// <c>BuildRecordingProvider</c> does for outbound continuation cases.
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
