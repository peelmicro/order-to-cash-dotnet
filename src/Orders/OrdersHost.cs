using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Health;
using OrderToCash.Orders.Infrastructure.Observability;

namespace OrderToCash.Orders;

/// <summary>
/// The Orders service's composition root — factored out of <c>Program.cs</c>
/// (review D6, round 2) so a test can drive the SAME method
/// <c>Program.cs</c> calls, rather than reconstructing its own copy of the
/// wiring. <c>OrdersDispatcherRegistrationTests.RealHostComposition_...</c>
/// (round 1) called <c>BuildServiceProvider</c> with its OWN
/// <see cref="ServiceProviderOptions"/>, which proved the container refuses
/// a broken graph when asked to validate — not whether <c>Program.cs</c>
/// asks. Factoring this method out closes that gap: the flags live in
/// exactly one place, the test calls <see cref="CreateBuilder"/> and then
/// the returned builder's own <c>Build()</c>, and reverting the flags here
/// is the only way to make that call stop validating — which is exactly
/// what round 2's Q1 mutation (<c>ValidateOnBuild = true,</c> →
/// <c>ValidateOnBuild = false,</c>) now fails a real test rather than
/// leaving the suite green.
/// </summary>
public static class OrdersHost
{
    public static HostApplicationBuilder CreateBuilder(
        string[] args,
        Action<OrdersOutboxOptions> configureOutbox,
        Action<OrdersAcceptanceOptions> configureAcceptance,
        Action<OrdersSagaOptions> configureSaga,
        Action<TelemetryOptions>? configureTelemetry = null,
        Action<HealthOptions>? configureHealth = null)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // design.md §5.1 — registered BEFORE anything else, so every span
        // and metric this host later creates is already under a live
        // TracerProvider/MeterProvider. Optional (defaults to the
        // TelemetryOptions default endpoint) so every pre-existing test
        // that drives this method for a reason unrelated to telemetry is
        // undisturbed; Program.cs always passes the real
        // OrdersProgramConfiguration.ConfigureTelemetry delegate.
        builder.Services.AddOrdersTelemetry(configureTelemetry ?? (_ => { }));

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

        // review D3: Host.CreateApplicationBuilder only turns ValidateOnBuild /
        // ValidateScopes ON when the environment is Development
        // (HostingHostBuilderExtensions' own default), so the same missing-port
        // misconfiguration that is loud in a developer's shell is SILENT wherever
        // ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT is unset — which is every
        // container this repository's compose files and CI run, since Production
        // is the environment's own default when neither variable is set. Forcing
        // both unconditionally makes a missing or duplicated port fail Build()
        // exactly like a missing or duplicated command handler already does
        // (CLAUDE.md: "every port is registered explicitly ... the startup
        // validation pass is what turns 'a handler is missing' from a runtime
        // surprise into a boot failure ... DI failures must be loud at boot").
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddOrdersOutbox(configureOutbox);
        builder.Services.AddOrdersAcceptance(configureAcceptance);

        // AddOrdersSaga runs after the two calls above (it reuses their
        // singleton INatsConnection and scoped OrdersDbContext/IClock/
        // IUnitOfWork — design.md §9) and BEFORE AddDispatcher, for the same
        // reason the other two extensions already precede it: every port a
        // handler needs must be registered before the dispatcher's
        // validation pass runs.
        builder.Services.AddOrdersSaga(configureSaga);

        // design.md §8 (group A4) — OPT-IN: only registered when a real
        // delegate is supplied. Program.cs always passes
        // OrdersProgramConfiguration.ConfigureHealth; the many pre-existing
        // fixtures/tests that build this host for an unrelated reason
        // (OrdersDispatcherRegistrationTests, SagaIntegrationTestSupport,
        // etc.) pass none and stay completely undisturbed — unlike
        // telemetry, HealthProbeService binds a REAL Kestrel port the
        // moment the host is started, so making it unconditional would risk
        // port collisions across every test that starts this host for a
        // reason that has nothing to do with this feature.
        if (configureHealth is not null)
        {
            builder.Services.AddOrdersHealth(configureHealth);
        }

        // AddDispatcher MUST run after the three calls above so every port
        // PlaceOrderCommandHandler and the ten saga fact command handlers
        // need is already registered, and it throws
        // DispatcherValidationException SYNCHRONOUSLY if a command has zero
        // or more than one handler — a boot failure, never a first-dispatch
        // surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
