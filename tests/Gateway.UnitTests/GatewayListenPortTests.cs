using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using OrderToCash.Gateway;
using OrderToCash.Gateway.Infrastructure;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Backlog id 96 — the second half of "reads GATEWAY_PORT … and the host
/// listens on it": <see cref="GatewayHost.CreateBuilder"/> turns
/// <see cref="GatewayOptions.Port"/> into Kestrel's listen address, unless an
/// explicit URL (<c>--urls</c>, <c>ASPNETCORE_URLS</c>) is configured — the
/// precedence every in-process test host relies on to take an ephemeral port
/// instead of colliding on 3001. Shares the environment-variable collection
/// because it mutates <c>ASPNETCORE_URLS</c>.
/// </summary>
[Collection(GatewayEnvironmentVariableTestCollection.Name)]
public sealed class GatewayListenPortTests
{
    private static readonly string[] _urlEnvVars = ["ASPNETCORE_URLS", "DOTNET_URLS", "ASPNETCORE_HTTP_PORTS", "DOTNET_HTTP_PORTS"];

    private static void ClearUrlEnv()
    {
        foreach (var name in _urlEnvVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static Action<GatewayOptions> Configure(int? port) => options =>
    {
        options.Port = port;
        options.Nats.Url = "nats://127.0.0.1:1";
        options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
        options.Mongo.Database = "otc_read_model_listen_port_probe";
    };

    /// <summary>
    /// ⚑ARM — the real socket. <see cref="GatewayOptions.Port"/> is 0 (the OS
    /// picks the port) rather than a pre-chosen free port: backlog id 85
    /// retired the open-a-listener-to-find-a-free-port helper repository-wide,
    /// and <c>ContainerFixtureHostPortAssignmentTests</c> fails the build if it
    /// returns. Kestrel's own default is <c>http://localhost:5000</c>, so a host
    /// that ignored the option reports that address and never an
    /// every-interface one; the exact-number propagation is
    /// <see cref="CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured"/>'s claim.
    /// </summary>
    [Fact]
    public async Task StartedHost_ListensOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured()
    {
        ClearUrlEnv();
        var app = GatewayHost.Configure(GatewayHost.CreateBuilder([], Configure(0)).Build());
        try
        {
            await app.StartAsync(CancellationToken.None);

            var address = Assert.Single(app.Urls);
            Assert.Matches(@"^http://(\[::\]|0\.0\.0\.0):[1-9][0-9]*$", address);
            var port = new Uri(address).Port;

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var response = await client.GetAsync("/health/live", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured()
    {
        ClearUrlEnv();
        var builder = GatewayHost.CreateBuilder([], Configure(13001));

        Assert.Equal("http://+:13001", builder.Configuration[WebHostDefaults.ServerUrlsKey]);
    }

    /// <summary>The seam every in-process test host uses: an explicit <c>--urls</c> wins over <see cref="GatewayOptions.Port"/>, so a test host takes its ephemeral port and never 3001.</summary>
    [Fact]
    public void CreateBuilder_LeavesAnExplicitUrlsArgumentUntouched_EvenWhenAPortIsConfigured()
    {
        ClearUrlEnv();
        var builder = GatewayHost.CreateBuilder(["--urls", "http://127.0.0.1:0"], Configure(3001));

        Assert.Equal("http://127.0.0.1:0", builder.Configuration[WebHostDefaults.ServerUrlsKey]);
    }

    /// <summary>ASPNETCORE_URLS is the platform's explicit full-URL override and wins over GATEWAY_PORT.</summary>
    [Fact]
    public void CreateBuilder_LeavesAnExplicitAspNetCoreUrlsVariableUntouched_EvenWhenAPortIsConfigured()
    {
        ClearUrlEnv();
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:14999");
        try
        {
            var builder = GatewayHost.CreateBuilder([], Configure(3001));

            Assert.Equal("http://127.0.0.1:14999", builder.Configuration[WebHostDefaults.ServerUrlsKey]);
        }
        finally
        {
            ClearUrlEnv();
        }
    }

    /// <summary>ASPNETCORE_HTTP_PORTS is NOT an override: the official ASP.NET Core images set it to 8080 by default, which would otherwise silently defeat GATEWAY_PORT in a container.</summary>
    [Fact]
    public void CreateBuilder_StillBindsGatewayOptionsPort_WhenOnlyAspNetCoreHttpPortsIsSet()
    {
        ClearUrlEnv();
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", "8080");
        try
        {
            var builder = GatewayHost.CreateBuilder([], Configure(13001));

            Assert.Equal("http://+:13001", builder.Configuration[WebHostDefaults.ServerUrlsKey]);
        }
        finally
        {
            ClearUrlEnv();
        }
    }

    /// <summary>A configure delegate that chooses no port leaves Kestrel's own configuration alone — no binding is invented.</summary>
    [Fact]
    public void CreateBuilder_SetsNoUrls_WhenNoPortIsConfigured()
    {
        ClearUrlEnv();
        var builder = GatewayHost.CreateBuilder([], Configure(null));

        Assert.Null(builder.Configuration[WebHostDefaults.ServerUrlsKey]);
    }
}
