using OrderToCash.Notifications;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="NotificationsProgramConfiguration.Configure"/>, the EXACT
/// method <c>Program.cs</c> calls, so deleting any of its eleven environment
/// reads — including the unconditional <c>SenderKind = Smtp</c> switch that
/// makes this the one caller in the repository that sends real mail — fails
/// a named test instead of leaving the suite green.
/// </summary>
[Collection(NotificationsEnvironmentVariableTestCollection.Name)]
public sealed class NotificationsProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_DB_NOTIFICATIONS", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD",
        "KAFKA_BOOTSTRAP_SERVERS", "KAFKA_HOST_PORT",
        "NOTIFICATIONS_SMTP_HOST", "NOTIFICATIONS_SMTP_PORT", "MAILPIT_SMTP_HOST_PORT", "NOTIFICATIONS_SMTP_FROM_ADDRESS",
        "ORDERS_HEALTH_PORT", "FULFILLMENT_HEALTH_PORT", "BILLING_HEALTH_PORT", "NOTIFICATIONS_HEALTH_PORT", "PROJECTOR_HEALTH_PORT",
        "FACT_RETRY_MAX_ATTEMPTS", "FACT_RETRY_BACKOFF_MS",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Configure_BuildsEveryDocumentedDefault_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=localhost,1433;Database=otc_notifications;User Id=otc_app;Password=dev-password;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
            Assert.Equal(NotificationSenderKind.Smtp, options.SenderKind);
            Assert.Equal("localhost", options.Smtp.Host);
            Assert.Equal(1025, options.Smtp.Port);
            Assert.Equal("no-reply@order-to-cash.example", options.Smtp.FromAddress);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_HOST", "sql-box");
        Environment.SetEnvironmentVariable("MSSQL_HOST_PORT", "14330");
        Environment.SetEnvironmentVariable("MSSQL_DB_NOTIFICATIONS", "custom_notifications_db");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_SMTP_HOST", "smtp-box");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_SMTP_PORT", "2525");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_SMTP_FROM_ADDRESS", "custom@example.com");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=sql-box,14330;Database=custom_notifications_db;User Id=custom_user;Password=s3cr3t;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
            Assert.Equal(NotificationSenderKind.Smtp, options.SenderKind);
            Assert.Equal("smtp-box", options.Smtp.Host);
            Assert.Equal(2525, options.Smtp.Port);
            Assert.Equal("custom@example.com", options.Smtp.FromAddress);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToMailpitSmtpHostPort_WhenNotificationsSmtpPortIsUnsetButMailpitPortIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("MAILPIT_SMTP_HOST_PORT", "1030");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal(1030, options.Smtp.Port);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToKafkaHostPortVariable_WhenTheExplicitBootstrapServersIsUnset()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("KAFKA_HOST_PORT", "9099");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal("localhost:9099", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// Review round 2, D5 rows 4–5 — #7's <c>fact-retry-dispatcher.spec</c>
    /// asserts BOTH halves of the family: the substitution above, and the
    /// DEFAULTS (<c>3</c>/<c>500</c>) when neither env var is set. Only
    /// <c>OrdersProgramConfigurationTests</c> carried this half; a changed
    /// fallback in <c>NotificationsProgramConfiguration.cs</c> went
    /// unnoticed by the whole suite.
    /// </summary>
    [Fact]
    public void Configure_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal(3, options.FactRetry.MaxAttempts);
            Assert.Equal(500, options.FactRetry.BackoffMs);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// D3 (review round 1) — design.md §3.5's two-variable retry policy
    /// family, `FACT_RETRY_MAX_ATTEMPTS`/`FACT_RETRY_BACKOFF_MS`, was
    /// unguarded here: <c>OrdersProgramConfigurationTests</c> already
    /// applies this SUBSTITUTION convention (CLAUDE.md's sibling-family
    /// rule) but Notifications' own read had no test at all. Set to
    /// distinct, non-default, mutually non-interchangeable values, so
    /// repointing either read at the other's variable name fails THIS
    /// assertion naming the wrong value — never merely "a value was read".
    /// </summary>
    [Fact]
    public void Configure_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS", "7");
        Environment.SetEnvironmentVariable("FACT_RETRY_BACKOFF_MS", "250");
        try
        {
            var options = new NotificationsOptions();
            NotificationsProgramConfiguration.Configure(options);

            Assert.Equal(7, options.FactRetry.MaxAttempts);
            Assert.Equal(250, options.FactRetry.BackoffMs);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_Throws_WhenMsSqlAppPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var options = new NotificationsOptions();
            var exception = Record.Exception(() => NotificationsProgramConfiguration.Configure(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>design.md §8.1/§9.2, group A4 — NOTIFICATIONS_HEALTH_PORT defaults to 3005 when unset.</summary>
    [Fact]
    public void ConfigureHealth_DefaultsPortTo3005_WhenNoEnvVarIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new HealthOptions();
            NotificationsProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(3005, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — substitution (tasks.md A4b, ledger L28): every sibling
    /// <c>*_HEALTH_PORT</c> is set to a distinct, non-default value before
    /// asserting.
    /// </summary>
    [Fact]
    public void ConfigureHealth_ReadsNotificationsHealthPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("ORDERS_HEALTH_PORT", "23002");
        Environment.SetEnvironmentVariable("FULFILLMENT_HEALTH_PORT", "23003");
        Environment.SetEnvironmentVariable("BILLING_HEALTH_PORT", "23004");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT", "13005");
        Environment.SetEnvironmentVariable("PROJECTOR_HEALTH_PORT", "23006");
        try
        {
            var options = new HealthOptions();
            NotificationsProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(13005, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }
}
