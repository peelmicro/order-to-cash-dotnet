using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Health;
using OrderToCash.Fulfillment.Infrastructure.Observability;

namespace OrderToCash.Fulfillment;

/// <summary>
/// The Fulfillment service's composition root — the <c>OrdersHost</c> shape
/// (design.md §10.3), factored out of <c>Program.cs</c> so a test can drive
/// the SAME method <c>Program.cs</c> calls.
/// </summary>
public static class FulfillmentHost
{
    public static HostApplicationBuilder CreateBuilder(
        string[] args,
        Action<FulfillmentOptions> configure,
        Action<TelemetryOptions>? configureTelemetry = null,
        Action<HealthOptions>? configureHealth = null)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // design.md §5.1 — registered BEFORE anything else. Optional so
        // every pre-existing test driving this method for an unrelated
        // reason is undisturbed; Program.cs always passes the real
        // FulfillmentProgramConfiguration.ConfigureTelemetry delegate.
        builder.Services.AddFulfillmentTelemetry(configureTelemetry ?? (_ => { }));

        // design.md §6 — R58/OR7: AddJsonConsole + IncludeScopes make
        // the trace fields render at all. D11 (review round 3) — the
        // explicit ActivityTrackingOptions setting below is NOT what turns
        // TraceId on: the generic host enables
        // ActivityTrackingOptions.TraceId by DEFAULT, so deleting this line
        // alone leaves TraceId on every log line unchanged. Setting it
        // explicitly to None is what removes the field — measured by a
        // deletion probe against this runtime (round 2, probes 3-4), never
        // read from framework source (ledger L25).
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true;
            o.UseUtcTimestamp = true;
        });
        builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        // ValidateOnBuild/ValidateScopes forced ON in EVERY environment —
        // Host.CreateApplicationBuilder only turns them on when the
        // environment is Development, which is nowhere this repository
        // actually runs (review D3 of feature 15, inherited unchanged).
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddFulfillment(configure);

        // design.md §8 (group A4) — OPT-IN, the same reasoning as Orders'
        // own copy: only registered when a real delegate is supplied, so
        // pre-existing fixtures that build this host for an unrelated
        // reason never bind a real Kestrel port they never asked for.
        if (configureHealth is not null)
        {
            builder.Services.AddFulfillmentHealth(configureHealth);
        }

        // AddDispatcher runs LAST, so a missing or duplicated command/query
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
