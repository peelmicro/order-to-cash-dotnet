using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderToCash.Notifications.Application;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using OrderToCash.Notifications.Infrastructure.Notification;
using OrderToCash.Notifications.Infrastructure.Persistence;
using OrderToCash.Notifications.Presentation;

namespace OrderToCash.Notifications.Infrastructure;

/// <summary>
/// <c>AddNotifications(IServiceCollection, Action&lt;NotificationsOptions&gt;)</c>
/// — one explicit registration line per port, no assembly scan (CLAUDE.md:
/// "every port is registered explicitly"). The <c>AddFulfillment</c>/
/// <c>AddBilling</c> shape.
/// </summary>
public static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(this IServiceCollection services, Action<NotificationsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NotificationsOptions();
        configure(options);

        services.AddDbContext<NotificationsDbContext>(db => db.UseSqlServer(options.ConnectionString));
        // ProcessedEventLedger and IdempotentConsumer (the CANONICAL copy)
        // take DbContext, never NotificationsDbContext — this is the one
        // line that makes the scoped NotificationsDbContext resolvable that
        // way, mirroring AddOrdersOutbox exactly.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddSingleton<IOptions<NotificationsKafkaOptions>>(Options.Create(options.Kafka));
        services.AddSingleton<IOptions<NotificationsSmtpOptions>>(Options.Create(options.Smtp));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

        services.AddScoped<ProcessedEventLedger>();
        services.AddScoped<IdempotentConsumer>();
        services.AddScoped<INotificationIdempotency, NotificationIdempotency>();

        services.AddScoped<NotificationDispatchService>();

        // IFactStreamSubscriber: SINGLETON, not scoped — NotificationFactsConsumer
        // is itself a singleton BackgroundService (every AddHostedService
        // is), and ValidateOnBuild refuses a singleton that directly
        // consumes a scoped service. KafkaFactStreamSubscriber holds no
        // scoped state of its own — the IConsumer client it builds is
        // created fresh INSIDE ConsumeAsync and disposed with it — so
        // singleton loses nothing (same reasoning as Orders' own
        // registration).
        services.AddSingleton<IFactStreamSubscriber, KafkaFactStreamSubscriber>();

        // The notification-sender port — ONE binding, chosen by
        // NotificationsOptions.SenderKind rather than inferred from whether
        // a credential happens to be present (NotificationsSmtpOptions'
        // own header explains why Mailpit does not need that rule). Every
        // automated test leaves SenderKind at its Console default; only
        // Program.cs sets it to Smtp.
        switch (options.SenderKind)
        {
            case NotificationSenderKind.Smtp:
                services.AddSingleton<Func<ISmtpTransport>>(_ => static () => new MailKitSmtpTransport());
                services.AddSingleton<INotificationSender, MailKitNotificationSender>();
                break;

            case NotificationSenderKind.Console:
            default:
                services.AddSingleton<INotificationSender, ConsoleNotificationSender>();
                break;
        }

        services.AddHostedService<NotificationFactsConsumer>();

        return services;
    }
}
