using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Projection;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Review round 2, D5 rows 44–46 — design.md §11: row 44 ("a real inbound
/// request produces a real server span") and rows 45–46 ("problem-json
/// carries the real active trace id; omits it rather than rendering
/// 'undefined'") were classified "Ported" with NO Gateway test able to
/// observe a trace at all before this round — a zero-hit search
/// (<c>traceparent|\.TraceId|TraceId\b</c> over <c>tests/Gateway.*Tests</c>).
/// design.md §11's own ruling for rows 45–46 is "Ported → into the
/// Gateway's log-capture case" — NOT the HTTP response body: #7's own
/// <c>problem-json.filter.ts</c> puts <c>traceId</c> only in the
/// <c>console.error</c> JSON line, never in <c>ProblemBody</c>, and #8's
/// <c>ProblemJsonMiddleware</c> matches that (no <c>traceId</c> key
/// anywhere in the response). The mechanism is already wired in
/// <c>GatewayHost.cs</c> (<c>AddJsonConsole</c> + <c>IncludeScopes</c> +
/// <c>ActivityTrackingOptions.TraceId | SpanId</c>, ledger L25) — this file
/// is the FIRST test to observe it.
/// </summary>
public sealed class LogCorrelationTests
{
    /// <summary>
    /// Rows 44–45 together: row 44 is proved through the GATEWAY HOST'S OWN
    /// <c>TracerProvider</c> — the SAME one <c>AddGatewayTelemetry</c>
    /// configures in <c>Telemetry.cs</c>, with a <see cref="RecordingActivityExporter"/>
    /// appended to it via <c>services.ConfigureOpenTelemetryTracerProvider(...)</c>
    /// in <c>GatewayTestHost.StartAsync</c>'s <c>overrideServices</c>
    /// callback — never a test-owned <see cref="ActivityListener"/> on
    /// <c>"Microsoft.AspNetCore"</c>.
    ///
    /// D10 (review round 3) — the PREVIOUS version of this test registered
    /// its OWN <c>ActivityListener</c> sourced to ASP.NET Core's hosting
    /// <see cref="ActivitySource"/>. ASP.NET Core starts the per-request
    /// hosting <c>Activity</c> whenever ANY listener is subscribed to that
    /// source (a framework behaviour, not something
    /// <c>AddAspNetCoreInstrumentation()</c> is needed for) — so that
    /// listener made row 44 pass regardless of whether
    /// <c>Telemetry.cs:68</c>'s own <c>.AddAspNetCoreInstrumentation()</c>
    /// call was present. Probe C3 (deleting that call) left the OLD test
    /// green. This version instead asserts a span was EXPORTED through the
    /// host's real pipeline, which requires the real registration: without
    /// it, ASP.NET Core's own hosting <c>ActivitySource</c> is never added
    /// to the host's <c>TracerProvider</c>, so nothing the host starts ever
    /// reaches this exporter, whether or not some OTHER listener elsewhere
    /// causes the framework to start the Activity in the first place.
    ///
    /// The failure log line produced while handling that SAME request (via
    /// <c>ProblemJsonMiddleware</c>'s <c>LogWarning</c>, itself inside
    /// <c>CorrelationLoggingScopeMiddleware</c>'s scope) then carries THAT
    /// span's own trace id, never merely "some non-empty value" — row 45.
    /// </summary>
    [Fact]
    public async Task Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId()
    {
        using var exporter = new RecordingActivityExporter();
        using var capture = new CapturedConsole();

        using (capture.Redirect())
        {
            var rpc = new RecordingRpcClient();
            var readModel = new InMemoryOrderReadModel();

            var gateway = await GatewayTestHost.StartAsync(
                options =>
                {
                    options.Nats.Url = "nats://127.0.0.1:1";
                    options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                    options.Mongo.Database = "otc_read_model_orders_it_unused";
                },
                overrideServices: services =>
                {
                    services.RemoveAll<IRpcClient>();
                    services.AddSingleton<IRpcClient>(rpc);
                    services.RemoveAll<IOrderReadModel>();
                    services.AddSingleton<IOrderReadModel>(readModel);
                    // D10 — appended to the HOST'S OWN TracerProviderBuilder
                    // (the one Telemetry.cs's AddGatewayTelemetry already
                    // configures), never a second, test-built provider.
                    services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
                });
            await using var disposeGateway = gateway;

            var login = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
            var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["accessToken"].ToString()!;
            gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // The same malformed-order-id 400 VALIDATION_FAILED path
            // ProblemJsonCorrelationTests already drives — no downstream
            // call, so no real NATS/Mongo dependency is needed.
            var response = await gateway.Client.GetAsync("/orders/not-a-guid-at-all");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // A short settle so the JSON console's own background writer
            // thread has drained its queue, and so the SimpleActivityExportProcessor
            // has exported the hosting Activity (it exports synchronously
            // on ActivityStopped) before this test reads either capture.
            await Task.Delay(500);
        }

        var serverSpans = exporter.Exported.Where(a => a.Kind == ActivityKind.Server).ToList();
        Assert.NotEmpty(serverSpans); // row 44 — the HOST's OWN registration produced AND exported a real server span.
        var expectedTraceId = serverSpans[^1].TraceId.ToString();

        var records = capture.ParseJsonLines();
        var mine = records.Where(r => ScopeValue(r, "TraceId") == expectedTraceId).ToList();
        Assert.NotEmpty(mine); // row 45 — the failure log line carries that SAME real trace id.
    }

    /// <summary>
    /// Row 46 — the omission half. A log line produced with NO active
    /// <see cref="Activity"/> (outside any request) carries no
    /// <c>TraceId</c>/<c>SpanId</c> scope key at all — never rendered as
    /// <c>""</c> or the literal string <c>"undefined"</c> — the SAME
    /// mechanism the other five services' own
    /// <c>LogCorrelationTests.R58_OR7_OmitsTheTraceFieldsEntirely…</c>
    /// cases prove.
    /// </summary>
    [Fact]
    public async Task Row46_OmitsTheTraceFieldsEntirelyRatherThanRenderingThemEmpty_WhenNoSpanIsActive()
    {
        using var capture = new CapturedConsole();

        using (capture.Redirect())
        {
            var gateway = await GatewayTestHost.StartAsync(options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_orders_it_unused_nospan";
            });
            await using var disposeGateway = gateway;

            var logger = gateway.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("OrderToCash.Gateway.IntegrationTests.NoSpanProbe");

            Assert.Null(Activity.Current);
            logger.LogInformation("no-span-probe {Marker}", "Row46_no_span_marker");

            await Task.Delay(300);
        }

        var records = capture.ParseJsonLines();
        var probe = Assert.Single(records, r => r.RootElement.TryGetProperty("Message", out var m) && m.GetString()!.Contains("Row46_no_span_marker", StringComparison.Ordinal));

        Assert.True(probe.RootElement.TryGetProperty("Scopes", out var scopes));
        foreach (var scope in scopes.EnumerateArray())
        {
            Assert.False(scope.TryGetProperty("TraceId", out _));
            Assert.False(scope.TryGetProperty("SpanId", out _));
        }
    }

    private static string? ScopeValue(System.Text.Json.JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        public Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to be called by this test's own request.");
    }

    private sealed class InMemoryOrderReadModel : IOrderReadModel
    {
        public Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken) => Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken) => Task.FromResult(new OrderListResult([], 0));
    }
}
