using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// D2 (review round 1) — R58/OR7, design.md §6, ledger L25, captured from
/// a FULLY configured real host (<c>ProjectorHost.CreateBuilder</c>'s own
/// <c>AddJsonConsole</c> + <c>IncludeScopes</c> +
/// <c>ActivityTrackingOptions</c> wiring), never asserted against the
/// logger abstraction — driving the SAME poison-fact-then-dead-letter flow
/// <c>ProjectorDeadLetterTests</c> proves at the broker level; here the
/// claim is about the CONSOLE output. Deliberately publishes onto
/// <see cref="ProjectorFactTopics.FulfillmentFacts"/> (as
/// <c>stock.reserved.v1</c> — Projector's own subscription-list shape,
/// <c>PR1</c>, means every fact type on every topic is handled, unlike
/// Notifications) rather than <c>ProjectorDeadLetterTests</c>' own
/// <see cref="ProjectorFactTopics.OrdersFacts"/> — that test's
/// <c>ConsumeOneAsync</c> reads its <c>.dlq</c> topic by "first non-EOF
/// message found", not by matching content, so a second poison publisher
/// on the SAME topic can steal its assertion. A DIFFERENT topic makes the
/// two tests genuinely independent rather than order-dependent.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class LogCorrelationTests(MongoContainerFixture mongoFixture, KafkaContainerFixture kafkaFixture, NatsContainerFixture natsFixture)
{
    private const string SourceTopic = ProjectorFactTopics.FulfillmentFacts;
    private const string DlqTopic = SourceTopic + ".dlq";

    [Fact]
    public async Task R58_OR7_EveryRecordProducedWhileHandlingAPoisonFactCarriesTheSameCorrelationIdAndTheSameTraceId()
    {
        await EnsureDlqTopicExistsAsync();

        using var capture = new CapturedConsole();
        Guid correlationId;

        using (capture.Redirect())
        {
            var host = await ProjectorTestHost.StartAsync(
                kafkaFixture, natsFixture, mongoFixture.ConnectionString, "otc_rm_logcorr_" + Guid.NewGuid().ToString("N"),
                configure: options =>
                {
                    options.FactRetry.MaxAttempts = 2;
                    options.FactRetry.BackoffMs = 100;
                    options.DeadLetter.BootstrapServers = kafkaFixture.BootstrapServers;
                });

            try
            {
                var orderId = Guid.NewGuid();
                correlationId = orderId;
                var poisonEnvelope = new Envelope<string>(Guid.NewGuid(), "stock.reserved.v1", orderId, correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");

                await ProjectorTestHost.PublishAsync(kafkaFixture, SourceTopic, poisonEnvelope);

                // Poll the CAPTURED console text itself — never the
                // broker's .dlq topic, which is ProjectorDeadLetterTests'
                // own claim — until at least two records for this
                // correlationId have been written: one retry warning, one
                // final dead-letter error.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
                List<JsonDocument> mine = [];
                while (DateTime.UtcNow < deadline)
                {
                    mine = capture.ParseJsonLines().Where(r => ScopeValue(r, "correlationId") == correlationId.ToString()).ToList();
                    if (mine.Count > 1)
                    {
                        break;
                    }

                    await Task.Delay(200);
                }

                Assert.True(mine.Count > 1, $"Expected more than one log record for this poison fact's correlationId; found {mine.Count}.");

                var traceIds = mine.Select(r => ScopeValue(r, "TraceId")).ToList();
                Assert.All(traceIds, t => Assert.False(string.IsNullOrEmpty(t)));
                Assert.Single(traceIds.Distinct(StringComparer.Ordinal));
            }
            finally
            {
                await ProjectorTestHost.StopHostAndWaitForGroupToClearAsync(host, kafkaFixture);
            }
        }
    }

    private async Task EnsureDlqTopicExistsAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafkaFixture.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = DlqTopic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private static string? ScopeValue(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }
}
