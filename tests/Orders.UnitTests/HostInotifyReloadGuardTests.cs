using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Guards the fix built during id 71 and recorded as its own backlog entry at
/// id 77 (<c>test_hosts_exhaust_the_per_user_inotify_limit</c>): root
/// <c>test.runsettings</c> disables the generic host's configuration
/// reload-on-change (<c>DOTNET_hostBuilder__reloadConfigOnChange=false</c>
/// under <c>RunConfiguration/EnvironmentVariables</c>), applied to every test
/// project via <c>Directory.Build.props</c>' <c>RunSettingsFilePath</c>.
///
/// Without it, every real host a test builds via <c>*Host.CreateBuilder</c>
/// opens one inotify instance (a <c>FileSystemWatcher</c>) per
/// <c>appsettings*.json</c>, and this repository builds ~12 such projects in
/// parallel under <c>quality.sh</c> — enough to exhaust this machine's
/// <c>fs.inotify.max_user_instances=128</c> (measured:
/// <c>progress/impl_operator_note_survives_the_compensation_branches.md</c>).
///
/// This test builds a REAL host the way the suite does — <see cref="OrdersHost.CreateBuilder"/>,
/// the SAME composition root <c>Program.cs</c> calls — and asserts the built
/// host's OWN <see cref="IConfiguration"/> reports
/// <c>hostBuilder:reloadConfigOnChange</c> as <c>"false"</c>. It never reads
/// or sets the process environment itself, which would prove only that the
/// test's own environment variable exists, not that the setting reaches a
/// host built the way the suite builds one.
/// </summary>
public sealed class HostInotifyReloadGuardTests
{
    /// <summary>
    /// Fails when the setting stops reaching test hosts. Armed two ways
    /// (<c>progress/impl_test_hosts_exhaust_the_per_user_inotify_limit.md</c>):
    /// deleting <c>Directory.Build.props</c>' <c>RunSettingsFilePath</c> line,
    /// and separately deleting <c>test.runsettings</c>' own
    /// <c>DOTNET_hostBuilder__reloadConfigOnChange</c> entry. Either mutation
    /// leaves the built host without the environment variable that seeds
    /// this configuration key, so the generic host's own default — reload ON
    /// — is what this assertion then observes and fails against.
    /// </summary>
    [Fact]
    public void RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled()
    {
        using var host = OrdersHost.CreateBuilder(
                args: [],
                configureOutbox: options =>
                {
                    // No real MS-SQL/Kafka needed — this test only inspects
                    // the built host's IConfiguration, it never dispatches a
                    // command or starts a background service (same fake
                    // addresses OrdersDispatcherRegistrationTests already
                    // proved boot with, with nothing reachable).
                    options.ConnectionString = "Server=localhost;Database=otc_orders_inotify_guard_probe;Trusted_Connection=True;";
                    options.Kafka.BootstrapServers = "127.0.0.1:1";
                },
                configureAcceptance: options => options.Nats.Url = "nats://127.0.0.1:1",
                configureSaga: options => options.Kafka.BootstrapServers = "127.0.0.1:1")
            .Build();

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var reloadSetting = configuration["hostBuilder:reloadConfigOnChange"];

        Assert.True(
            string.Equals(reloadSetting, "false", System.StringComparison.OrdinalIgnoreCase),
            "Expected the built host's own configuration key " +
            "'hostBuilder:reloadConfigOnChange' to be \"false\", set by root " +
            "test.runsettings' DOTNET_hostBuilder__reloadConfigOnChange under " +
            "RunConfiguration/EnvironmentVariables and applied to every test " +
            "project via Directory.Build.props' RunSettingsFilePath, but it " +
            $"was \"{reloadSetting ?? "<null>"}\". Every real host a test " +
            "builds opens one inotify instance per appsettings*.json watcher " +
            "when this setting is not disabled, and a parallel quality.sh " +
            "run exhausts this machine's fs.inotify.max_user_instances.");
    }
}
