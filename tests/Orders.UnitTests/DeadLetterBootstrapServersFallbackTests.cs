using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Messaging.DeadLetter;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Backlog id 104 — <c>DeadLetterKafkaOptions.BootstrapServers</c> used to
/// default independently to <c>"localhost:9092"</c>, so a host that
/// configured the saga's own <c>Kafka.BootstrapServers</c> (most test hosts)
/// still dead-lettered to whatever broker happened to be on
/// <c>localhost:9092</c> — a developer's persistent Kafka on that machine,
/// or nothing at all in CI. Proves the fix at the ONE call site every real
/// host goes through, <see cref="OrderToCash.Orders.OrdersHost.CreateBuilder"/>.
/// </summary>
public sealed class DeadLetterBootstrapServersFallbackTests
{
    [Fact]
    public void RealHostComposition_ConfiguredWithOnlySagaKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker()
    {
        const string sagaKafkaBootstrapServers = "saga-kafka-only.example:9092";

        var builder = OrderToCash.Orders.OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: options =>
            {
                options.ConnectionString = "Server=localhost;Database=otc_orders_dlq_fallback_probe;Trusted_Connection=True;";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            },
            configureAcceptance: options => options.Nats.Url = "nats://127.0.0.1:1",
            configureSaga: options =>
            {
                // Deliberately the ONLY Kafka-shaped setting on this
                // delegate — options.DeadLetter.BootstrapServers is left at
                // its own default (empty), which is the case this feature
                // fixes.
                options.Kafka.BootstrapServers = sagaKafkaBootstrapServers;
            });

        using var host = builder.Build();

        var resolved = host.Services.GetRequiredService<IOptions<DeadLetterKafkaOptions>>().Value;

        Assert.Equal(sagaKafkaBootstrapServers, resolved.BootstrapServers);
    }
}
