using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Health;

/// <summary>
/// design.md §8.1 — the second of the two hosting shapes considered, and
/// the one this design takes: a minimal <see cref="WebApplication"/> built
/// INSIDE <see cref="StartAsync"/>, mapping only <c>GET /health/live</c> and
/// <c>GET /health/ready</c>, rather than converting
/// <c>OrdersHost.CreateBuilder</c>'s return type from
/// <see cref="Microsoft.Extensions.Hosting.HostApplicationBuilder"/> to
/// <see cref="WebApplicationBuilder"/> for two endpoints. Needs
/// <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c>
/// (no NuGet package) and touches no existing signature.
/// </summary>
public sealed class HealthProbeService : IHostedService, IAsyncDisposable
{
    private readonly HealthOptions _options;
    private readonly IReadOnlyList<IHealthCheck> _checks;
    private WebApplication? _app;

    public HealthProbeService(IOptions<HealthOptions> options, IEnumerable<IHealthCheck> checks)
    {
        _options = options.Value;
        _checks = checks.ToList();
    }

    /// <summary>
    /// The port actually bound, read back from Kestrel's own resolved
    /// address after <see cref="StartAsync"/> — production always sets
    /// <see cref="HealthOptions.Port"/> to a real, fixed number; integration
    /// tests set <c>0</c> (an OS-assigned ephemeral port) so many parallel
    /// runs never collide, and read this property to find where to connect.
    /// </summary>
    public int? BoundPort { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        // A SEPARATE WebApplicationBuilder from the outer generic host's own
        // — its default console logging would otherwise double up on every
        // line the outer host's own AddJsonConsole already writes.
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{_options.Port}");

        _app = builder.Build();

        _app.MapGet("/health/live", () =>
        {
            var body = HealthCheckAggregator.Live();
            return Results.Text(JsonSerializer.Serialize(body, JsonWire.Options), "application/json");
        });

        _app.MapGet("/health/ready", async (CancellationToken ct) =>
        {
            var (statusCode, body) = await HealthCheckAggregator.ReadyAsync(_checks, ct).ConfigureAwait(false);
            return Results.Text(JsonSerializer.Serialize(body, JsonWire.Options), "application/json", statusCode: statusCode);
        });

        await _app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BoundPort = new Uri(address).Port;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }
}
