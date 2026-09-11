using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OrderToCash.Orders.Infrastructure.Observability;

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
    public const string SourceName = "OrderToCash.Orders";

    public static readonly ActivitySource Source = new(SourceName);
}

/// <summary>The ONE <see cref="Meter"/> every instrument in this service is created from (design.md §7).</summary>
public static class OtcMetrics
{
    public const string MeterName = "OrderToCash.Orders";

    public static readonly Meter Meter = new(MeterName);

    /// <summary><c>FactRetryDispatcher.DispatchAsync</c> entry→exit, tagged by consumer — recorded on the exhausted-retry/DLQ path as well as on success.</summary>
    public static readonly Histogram<double> FactProcessingLatencyMs = Meter.CreateHistogram<double>("otc_fact_processing_latency_ms");

    /// <summary><c>SagaFactHandler</c>, when a transition lands the order on <c>completed</c>/<c>cancelled</c>, measured from the order's own <c>order_date</c> — one instrument, tagged <c>outcome</c>.</summary>
    public static readonly Histogram<double> SagaCompletionMs = Meter.CreateHistogram<double>("otc_saga_completion_ms");

    /// <summary><c>OutboxRelay</c>, age of the oldest row with <c>published_at IS NULL</c> — a direct-write gauge (last-value semantics), recorded once per relay cycle.</summary>
    public static readonly Gauge<double> OutboxLagMs = Meter.CreateGauge<double>("otc_outbox_lag_ms");

    /// <summary><c>OutboxRelay</c>'s own cycle — <c>(high − low)</c> summed across every partition of every <c>.dlq</c> topic this service owns.</summary>
    public static readonly Gauge<long> DlqDepth = Meter.CreateGauge<long>("otc_dlq_depth");
}

/// <summary>design.md §9.2 — <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, read by <c>OrdersProgramConfiguration.ConfigureTelemetry</c>.</summary>
public sealed class TelemetryOptions
{
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}

/// <summary>
/// design.md §5.1 — registered from <c>OrdersHost.CreateBuilder</c> BEFORE
/// anything else. <c>OR5</c>: OTLP export only, no Prometheus scrape
/// endpoint anywhere — services are distinguished by the OTel
/// <c>service.name</c> resource attribute, not by a scrape target.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersTelemetry(this IServiceCollection services, Action<TelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TelemetryOptions();
        configure(options);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "orders"))
            .WithTracing(t => t
                .AddSource(OtcActivity.SourceName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)))
            .WithMetrics(m => m
                .AddMeter(OtcMetrics.MeterName)
                .AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint)));

        return services;
    }
}
