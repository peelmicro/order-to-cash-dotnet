using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Notifications;
using OrderToCash.Notifications.Application.Ports;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// <see cref="NotificationsHost.CreateBuilder"/> succeeds when every port is
/// registered and fails <c>Build()</c> (never only the first dispatch) when
/// one is removed — CLAUDE.md's "startup validation fails fast" rule.
/// <c>ValidateOnBuild</c>/<c>ValidateScopes</c> forced on in every
/// environment is what makes the negative case throw.
/// </summary>
public sealed class NotificationsDispatcherRegistrationTests
{
    [Fact]
    public void AddDispatcher_OverTheNotificationsAssembly_RegistersEveryCommandWithExactlyOneHandler()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddDispatcher(typeof(NotificationsHost).Assembly));

        Assert.Null(exception);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDispatcher));
    }

    [Fact]
    public void RealHostComposition_Build_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved()
    {
        var positiveBuilder = BuildRealHostBuilder();
        var positiveException = Record.Exception(() => positiveBuilder.Build());
        Assert.Null(positiveException);

        var negativeBuilder = BuildRealHostBuilder();
        var removed = negativeBuilder.Services.Single(d => d.ServiceType == typeof(INotificationSender));
        negativeBuilder.Services.Remove(removed);

        var negativeException = Record.Exception(() => negativeBuilder.Build());
        Assert.NotNull(negativeException);
        Assert.Contains(nameof(INotificationSender), negativeException!.ToString());
    }

    private static HostApplicationBuilder BuildRealHostBuilder() =>
        NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // No real MS-SQL/Kafka needed — ValidateOnBuild checks the
                // DI GRAPH, it does not connect (Orders'/Fulfillment's own
                // probe, inherited). SenderKind left at its Console default
                // deliberately — this test proves DI shape, not the sender
                // binding.
                options.ConnectionString = "Server=localhost;Database=otc_notifications_validate_on_build_probe;Trusted_Connection=True;";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            });
}
