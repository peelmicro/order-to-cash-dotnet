using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Infrastructure.Messaging.DeadLetter;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// Backlog id 104 — <c>DeadLetterKafkaOptions.BootstrapServers</c> used to
/// default independently to <c>"localhost:9092"</c>, so a host that
/// configured only <c>Kafka.BootstrapServers</c> (most test hosts) still
/// dead-lettered to whatever broker happened to be on <c>localhost:9092</c>.
/// Proves the fix at the ONE call site every real host goes through,
/// <see cref="OrderToCash.Projector.ProjectorHost.CreateBuilder"/>.
/// </summary>
public sealed class DeadLetterBootstrapServersFallbackTests
{
    [Fact]
    public void RealHostComposition_ConfiguredWithOnlyKafkaBootstrapServers_ResolvesTheDeadLetterProducerToTheSameBroker()
    {
        const string kafkaBootstrapServers = "projector-kafka-only.example:9092";

        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // Deliberately the ONLY Kafka-shaped setting —
                // options.DeadLetter.BootstrapServers is left at its own
                // default (empty), which is the case this feature fixes.
                options.Kafka.BootstrapServers = kafkaBootstrapServers;
                options.Nats.Url = "nats://localhost:4222";
                options.Mongo.ConnectionUri = "mongodb://user:pass@localhost:27017/?authSource=admin";
                options.Mongo.Database = "otc_read_model_dlq_fallback_probe";
            });

        using var host = builder.Build();

        var resolved = host.Services.GetRequiredService<IOptions<DeadLetterKafkaOptions>>().Value;

        Assert.Equal(kafkaBootstrapServers, resolved.BootstrapServers);
    }
}
