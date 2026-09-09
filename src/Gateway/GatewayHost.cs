using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Infrastructure;
using OrderToCash.Gateway.Presentation;
using OrderToCash.Gateway.Presentation.Endpoints;
using OrderToCash.Gateway.Presentation.Problem;
using OrderToCash.Gateway.Presentation.RateLimiting;

namespace OrderToCash.Gateway;

/// <summary>
/// The Gateway service's composition root — the <c>NotificationsHost</c>/
/// <c>ProjectorHost</c> shape, factored out of <c>Program.cs</c> so a test
/// can drive the SAME method <c>Program.cs</c> calls, retargeted at
/// <see cref="WebApplication"/> since this is the one service in this
/// repository that is an HTTP surface rather than a
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> host.
/// </summary>
public static class GatewayHost
{
    /// <summary>Registers every service and returns the builder WITHOUT calling <see cref="WebApplicationBuilder.Build"/> — the seam <c>GatewayDispatcherRegistrationTests</c> uses to prove the DI graph fails loudly (never only on first dispatch) when a port is removed, the same shape every other service's own <c>*Host.CreateBuilder</c> establishes.</summary>
    public static WebApplicationBuilder CreateBuilder(string[] args, Action<GatewayOptions> configure)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ValidateOnBuild/ValidateScopes forced ON in EVERY environment —
        // the same non-negotiable every other service's *Host class
        // enforces (WebApplicationBuilder only turns them on when the
        // environment is Development by default). WebApplicationBuilder
        // has no ConfigureContainer overload matching
        // HostApplicationBuilder's own (it forwards through
        // ConfigureHostBuilder, whose IHostBuilder.ConfigureContainer
        // shape takes an Action<TContainerBuilder>, not a factory) — the
        // documented equivalent for the Web-hosted builder is
        // UseDefaultServiceProvider.
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateOnBuild = true;
            o.ValidateScopes = true;
        });

        var options = new GatewayOptions();
        configure(options);

        builder.Services.AddGateway(options);
        builder.Services.AddLoginRateLimiter(options.LoginThrottle);

        // The app-wide JSON options — the SAME camelCase/nulls-omitted
        // OrderToCash.Contracts.Wire.JsonWire options every fact and every
        // RPC payload in this repository serialises through (CLAUDE.md:
        // "set once in a shared JsonSerializerOptions in Contracts so no
        // service can drift"), applied here to request binding AND
        // Results.Json response writing.
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonWire.Options.PropertyNamingPolicy;
            o.SerializerOptions.DictionaryKeyPolicy = JsonWire.Options.DictionaryKeyPolicy;
            o.SerializerOptions.DefaultIgnoreCondition = JsonWire.Options.DefaultIgnoreCondition;
            o.SerializerOptions.Encoder = JsonWire.Options.Encoder;
            o.SerializerOptions.PropertyNameCaseInsensitive = JsonWire.Options.PropertyNameCaseInsensitive;
            foreach (var converter in JsonWire.Options.Converters)
            {
                o.SerializerOptions.Converters.Add(converter);
            }
        });

        // AddDispatcher runs LAST, so a missing or duplicated command
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }

    /// <summary>Wires the middleware pipeline and maps every endpoint this feature builds — a separate step from <see cref="CreateBuilder"/> so a test can build the app WITHOUT ever starting Kestrel and still enumerate real endpoint metadata (<c>app.Services.GetRequiredService&lt;EndpointDataSource&gt;()</c>).</summary>
    public static WebApplication Configure(WebApplication app)
    {
        // Order matters: correlation id first (every later error log line
        // and Problem body needs it), problem-json translation next (so it
        // wraps everything after it, including auth failures and rate
        // limiting), THEN rate limiting (native ASP.NET Core middleware,
        // short-circuits before auth for the one route it guards), THEN
        // the bearer-auth check, THEN endpoint execution.
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ProblemJsonMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseMiddleware<Presentation.Auth.BearerAuthenticationMiddleware>();

        app.MapAuthEndpoints();
        app.MapOrdersEndpoints();
        // Registered AFTER MapOrdersEndpoints (which maps the parameter
        // route GET /orders/{id}) — the SAME order RoutePrecedenceTests
        // already probed and the ported-idiom ledger's row 6 licenses: a
        // literal segment (/orders/stream) always outranks a parameter
        // segment at the same position in ASP.NET Core Minimal API
        // routing, regardless of Map* call order.
        app.MapStreamEndpoints();
        app.MapStockEndpoints();
        app.MapInvoicesEndpoints();
        app.MapCreditsEndpoints();
        app.MapCatalogEndpoints();
        app.MapDocsEndpoints();

        return app;
    }

    /// <summary><see cref="CreateBuilder"/> + <see cref="WebApplicationBuilder.Build"/> + <see cref="Configure"/> in one call — what <c>Program.cs</c> uses; a test that needs to intervene between registration and <c>Build()</c> (or between <c>Build()</c> and pipeline wiring) calls the two steps directly instead.</summary>
    public static WebApplication Build(string[] args, Action<GatewayOptions> configure) =>
        Configure(CreateBuilder(args, configure).Build());
}
