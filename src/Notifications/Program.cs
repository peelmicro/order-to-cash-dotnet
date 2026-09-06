using Microsoft.Extensions.Hosting;
using OrderToCash.Notifications;
using OrderToCash.Notifications.Infrastructure;

// The Notifications service host — the FIRST runnable Notifications host
// (feature notifications_service). The actual composition (AddNotifications,
// AddDispatcher, the ValidateOnBuild/ValidateScopes forcing) lives in
// NotificationsHost.CreateBuilder, factored out the same way
// FulfillmentHost/BillingHost are.
var builder = NotificationsHost.CreateBuilder(
    args,
    configure: options =>
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";

        // The real host always sends real mail — Mailpit locally, per the
        // brief's "MailKit → Mailpit" instruction. Every automated test
        // leaves NotificationsOptions.SenderKind at its Console default
        // instead of calling NotificationsHost.CreateBuilder with this
        // configure delegate.
        options.SenderKind = NotificationSenderKind.Smtp;
        options.Smtp.Host = Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_HOST") ?? "localhost";
        options.Smtp.Port = int.TryParse(Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_PORT"), out var smtpPort)
            ? smtpPort
            : int.TryParse(Environment.GetEnvironmentVariable("MAILPIT_SMTP_HOST_PORT"), out var mailpitPort) ? mailpitPort : 1025;
        options.Smtp.FromAddress = Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_FROM_ADDRESS") ?? "no-reply@order-to-cash.example";
    });

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

// Mirrors NotificationsDbContextFactory's own reading of .env's variable
// names — the runtime host and the design-time migration tooling read the
// exact same environment, deliberately.
static string BuildMsSqlConnectionString()
{
    var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
    var database = Environment.GetEnvironmentVariable("MSSQL_DB_NOTIFICATIONS") ?? "otc_notifications";
    var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
    var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
        ?? throw new InvalidOperationException(
            "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the Notifications host, " +
            "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_NOTIFICATIONS|HOST_PORT)=' .env | xargs)");

    return $"Server={host},{port};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
}
