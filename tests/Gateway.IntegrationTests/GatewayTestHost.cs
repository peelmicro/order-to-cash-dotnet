using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Gateway;
using OrderToCash.Gateway.Infrastructure;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Boots the REAL <see cref="GatewayHost"/> graph over real Kestrel on an
/// ephemeral port, returning a live <see cref="HttpClient"/> — never
/// <c>TestServer</c>/<c>WebApplicationFactory</c>'s in-memory transport,
/// because the whole point of an integration suite for THIS feature is a
/// real socket round trip. <paramref name="overrideServices"/> runs AFTER
/// <see cref="GatewayHost.CreateBuilder"/> but BEFORE <c>Build()</c> — the
/// same seam <c>FulfillmentDispatcherRegistrationTests</c> uses to remove a
/// registration, used here to SUBSTITUTE a fake port (e.g. <c>IRpcClient</c>)
/// for tests that are not about the wire itself.
/// </summary>
public sealed class GatewayTestHost : IAsyncDisposable
{
    private const int MaxBindAttempts = 5;

    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// The real, running app's DI container — added for
    /// <c>StreamHttpTests.Disconnect_UnsubscribesFromTheHub_...</c> (ported-
    /// idiom ledger row 10, <c>progress/impl_gateway_sse_push.md</c>) to
    /// resolve the SAME singleton <see cref="OrderToCash.Gateway.Application.Stream.StreamHub"/>
    /// instance the running <c>/orders/stream</c> endpoint itself uses, so a
    /// teardown guard can observe <c>StreamHub.SubscriberCount</c> from
    /// OUTSIDE the endpoint while still exercising the endpoint's own real
    /// HTTP request/disconnect path.
    /// </summary>
    public IServiceProvider Services => _app!.Services;

    /// <summary>
    /// Found while verifying feature <c>gateway_sse_push</c>: under
    /// <c>dotnet test</c> on the WHOLE solution — six heavy
    /// <c>*.IntegrationTests</c> projects, each booting real Testcontainers,
    /// running as separate PROCESSES concurrently — a real Kestrel bind on
    /// an OS-assigned ephemeral port occasionally throws
    /// <see cref="TaskCanceledException"/> during the brief, genuine burst
    /// of contention at the very start of the run (never once observed when
    /// this project runs alone, and never a business-logic assertion
    /// failure — always this exact exception, always during
    /// <c>KestrelServerImpl.BindAsync</c>). <see cref="AssemblyBehavior"/>'s
    /// <c>[CollectionBehavior(DisableTestParallelization = true)]</c>
    /// removed the WITHIN-this-assembly instance of the race (many
    /// GatewayTestHost.StartAsync calls binding Kestrel at the same
    /// instant); this bounded, EXPLICITLY PACED retry (200ms × attempt,
    /// never a tight ~1ms loop — the exact defect
    /// <c>SagaIntegrationTestSupport.StartHostAsync</c>'s own history
    /// warns against) closes the remaining CROSS-project instance, which no
    /// change inside this one assembly could remove on its own. Narrowly
    /// scoped to <see cref="TaskCanceledException"/> only — a genuine DI
    /// configuration failure (e.g. a missing port registration) throws a
    /// DIFFERENT exception type and is never masked by a retry here.
    /// </summary>
    public static async Task<GatewayTestHost> StartAsync(Action<GatewayOptions> configure, Action<IServiceCollection>? overrideServices = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = OrderToCash.Gateway.GatewayHost.CreateBuilder(["--urls", "http://127.0.0.1:0"], configure);
            overrideServices?.Invoke(builder.Services);
            var app = OrderToCash.Gateway.GatewayHost.Configure(builder.Build());

            try
            {
                await app.StartAsync().ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (attempt < MaxBindAttempts)
            {
                await app.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
                continue;
            }

            var address = app.Urls.First();
            return new GatewayTestHost
            {
                _app = app,
                Client = new HttpClient { BaseAddress = new Uri(address) },
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
