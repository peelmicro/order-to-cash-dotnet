using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderToCash.Notifications.Infrastructure.Messaging.DeadLetter;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// Backlog id 104 — <c>DeadLetterKafkaOptions.BootstrapServers</c> used to
/// default independently to <c>"localhost:9092"</c>, so a host that
/// configured only <c>Kafka.BootstrapServers</c> (most test hosts) still
/// dead-lettered to whatever broker happened to be on <c>localhost:9092</c>.
/// Proves the fix at the ONE call site every real host goes through,
/// <see cref="OrderToCash.Notifications.NotificationsHost.CreateBuilder"/>.
/// </summary>
public sealed class DeadLetterBootstrapServersFallbackTests
{
    [Fact]
    public void RealHostComposition_ConfiguredWithOnlyKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker()
    {
        const string kafkaBootstrapServers = "notifications-kafka-only.example:9092";

        var builder = NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // No real MS-SQL needed — ValidateOnBuild checks the DI
                // GRAPH, it does not connect (inherited probe from
                // NotificationsDispatcherRegistrationTests). Deliberately the
                // ONLY Kafka-shaped setting — options.DeadLetter.BootstrapServers
                // is left at its own default (empty), which is the case this
                // feature fixes.
                options.ConnectionString = "Server=localhost;Database=otc_notifications_dlq_fallback_probe;Trusted_Connection=True;";
                options.Kafka.BootstrapServers = kafkaBootstrapServers;
            });

        using var host = builder.Build();

        var resolved = host.Services.GetRequiredService<IOptions<DeadLetterKafkaOptions>>().Value;

        Assert.Equal(kafkaBootstrapServers, resolved.BootstrapServers);
    }
}
