using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OrderToCash.Billing.Infrastructure.Observability;

/// <summary>
/// The ONE <see cref="ActivitySource"/> every span in this service is
/// started from (design.md §5.1, ledger L20). <c>AddSource</c> is
/// EXACT-MATCH opt-in — an <see cref="ActivitySource"/> whose name is not
/// registered on this service's own <c>TracerProvider</c> produces
/// <see cref="Activity"/> objects that are never sampled and never
/// exported, silently. <c>TelemetryWiringTests</c> enumerates every
/// <see cref="ActivitySource"/> construction under <c>src/</c> and asserts
/// each is registered on its own host's <c>AddSource</c> list.
/// </summary>
public static class OtcActivity
{
    public const string SourceName = "OrderToCash.Billing";

    public static readonly ActivitySource Source = new(SourceName);
}

/// <summary>The ONE <see cref="Meter"/> every instrument in this service is created from (design.md §7).</summary>
public static class OtcMetrics
{
    public const string MeterName = "OrderToCash.Billing";

    public static readonly Meter Meter = new(MeterName);

    /// <summary><c>OutboxRelay</c>, age of the oldest row with <c>published_at IS NULL</c> — a direct-write gauge (last-value semantics), recorded once per relay cycle.</summary>
    public static readonly Gauge<double> OutboxLagMs = Meter.CreateGauge<double>("otc_outbox_lag_ms");

    /// <summary><c>OutboxRelay</c>'s own cycle — this service owns no <c>.dlq</c> topic (design.md §13), so <see cref="OrderToCash.Billing.Infrastructure.Outbox.NoOpDlqDepthGauge"/> always records <c>0</c>.</summary>
    public static readonly Gauge<long> DlqDepth = Meter.CreateGauge<long>("otc_dlq_depth");
}

/// <summary>design.md §9.2 — <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, read by <c>BillingProgramConfiguration.ConfigureTelemetry</c>.</summary>
public sealed class TelemetryOptions
{
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}

/// <summary>
/// design.md §5.1 — registered from <c>BillingHost.CreateBuilder</c> BEFORE
/// anything else. <c>OR5</c>: OTLP export only, no Prometheus scrape
/// endpoint anywhere — services are distinguished by the OTel
/// <c>service.name</c> resource attribute, not by a scrape target.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddBillingTelemetry(this IServiceCollection services, Action<TelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TelemetryOptions();
        configure(options);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "billing"))
            .WithTracing(t => t
                .AddSource(OtcActivity.SourceName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)))
            .WithMetrics(m => m
                .AddMeter(OtcMetrics.MeterName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)));

        return services;
    }
}
