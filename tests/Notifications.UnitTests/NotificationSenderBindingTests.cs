using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// Review round 1, A2 (backlog id 73) — before this file, nothing told the
/// two <see cref="NotificationSenderKind"/> bindings apart: the reviewer's
/// M3 mutation swapped the decorator's <c>inner</c>/<c>fallback</c>
/// arguments in <c>NotificationsServiceCollectionExtensions.cs</c> (the
/// decorator aimed at the wrong sibling — <c>DegradingNotificationSender(consoleFallback,
/// smtpSender, …)</c>) and left <c>Notifications.UnitTests</c> 104/104
/// green. Builds the REAL host composition
/// (<c>NotificationsHost.CreateBuilder</c> — the same call
/// <c>NotificationsDispatcherRegistrationTests</c> already uses to prove DI
/// SHAPE without a real database/broker) and resolves the REAL
/// <see cref="INotificationSender"/> singleton for each binding.
/// </summary>
public sealed class NotificationSenderBindingTests
{
    [Fact]
    public void SenderKindConsole_ResolvesAnUnwrappedConsoleNotificationSender()
    {
        using var host = BuildHost(NotificationSenderKind.Console);

        var sender = host.Services.GetRequiredService<INotificationSender>();

        Assert.IsType<ConsoleNotificationSender>(sender);
    }

    /// <summary>
    /// The case the reviewer's M3 swap defeats: not merely "some
    /// <see cref="DegradingNotificationSender"/> was resolved", but that its
    /// OWN <see cref="DegradingNotificationSender.Inner"/> is the real
    /// MailKit sender — the property a swapped wiring gets backwards.
    /// </summary>
    [Fact]
    public void SenderKindSmtp_ResolvesADegradingNotificationSenderWhoseInnerIsTheMailKitSender()
    {
        using var host = BuildHost(NotificationSenderKind.Smtp);

        var sender = host.Services.GetRequiredService<INotificationSender>();

        var degrading = Assert.IsType<DegradingNotificationSender>(sender);
        Assert.IsType<MailKitNotificationSender>(degrading.Inner);
    }

    private static Microsoft.Extensions.Hosting.IHost BuildHost(NotificationSenderKind kind) =>
        NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // No real MS-SQL/Kafka needed — ValidateOnBuild checks the
                // DI GRAPH, it does not connect (the same probe
                // NotificationsDispatcherRegistrationTests already uses).
                options.ConnectionString = "Server=localhost;Database=otc_notifications_sender_binding_probe;Trusted_Connection=True;";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
                options.SenderKind = kind;
            }).Build();
}
