namespace OrderToCash.Notifications.Infrastructure;

/// <summary>Kafka consumer settings for the fact stream (mirrors <c>OrdersSagaKafkaOptions</c>'s shape).</summary>
public sealed class NotificationsKafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Bounded <c>Consume(TimeSpan)</c> poll — returns <see langword="null"/> when nothing arrived, so the cancellation token is observed every cycle.</summary>
    public int PollTimeoutMs { get; set; } = 1_000;
}

/// <summary>Which <c>INotificationSender</c> adapter is bound — feature 23's acceptance bullet 2's "same port, two adapters".</summary>
public enum NotificationSenderKind
{
    /// <summary>Structured console line, no I/O — the SAFE default, and what every automated test binds explicitly.</summary>
    Console,

    /// <summary>MailKit over SMTP, real network I/O — bound only by <c>Program.cs</c>, against Mailpit locally.</summary>
    Smtp,
}

/// <summary>
/// SMTP connection settings for <c>MailKitNotificationSender</c>. Mailpit
/// (<c>docker-compose.infra.yml</c>'s <c>mailpit</c> service) enforces no
/// authentication and needs no TLS locally — unlike #7's original Mailtrap
/// sandbox binding, which needed a credential-presence rule to decide
/// console vs real. This service does not: the adapter choice is
/// <see cref="NotificationSenderKind"/>, set explicitly by the composition
/// root, never inferred from whether a credential happens to be present.
/// </summary>
public sealed class NotificationsSmtpOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string FromAddress { get; set; } = "no-reply@order-to-cash.example";
}

/// <summary>The configuration <c>NotificationsServiceCollectionExtensions</c> needs.</summary>
public sealed class NotificationsOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NotificationsKafkaOptions Kafka { get; } = new();

    public NotificationsSmtpOptions Smtp { get; } = new();

    /// <summary>
    /// Defaults to <see cref="NotificationSenderKind.Console"/> — the safe
    /// default an accidental caller (a test that forgot to configure this
    /// option at all) gets. <c>Program.cs</c> is the one caller that sets
    /// this to <see cref="NotificationSenderKind.Smtp"/>.
    /// </summary>
    public NotificationSenderKind SenderKind { get; set; } = NotificationSenderKind.Console;
}
