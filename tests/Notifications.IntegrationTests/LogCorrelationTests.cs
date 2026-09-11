using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// D2 (review round 1) — R58/OR7, design.md §6, ledger L25, captured from
/// a FULLY configured real host (<c>NotificationsHost.CreateBuilder</c>'s
/// own <c>AddJsonConsole</c> + <c>IncludeScopes</c> +
/// <c>ActivityTrackingOptions</c> wiring), never asserted against the
/// logger abstraction — driving the SAME poison-fact-then-dead-letter flow
/// <c>NotificationDeadLetterTests</c> proves at the broker level; here the
/// claim is about the CONSOLE output. Deliberately publishes onto
/// <see cref="NotificationFactTopics.BillingFacts"/> (as <c>invoice.issued.v1</c>
/// — a type this consumer's own routing table handles, unlike
/// <c>stock.reserved.v1</c> on <c>FulfillmentFacts</c>, which this service
/// silently ignores without ever attempting deserialisation) rather than
/// <c>NotificationDeadLetterTests</c>' own <see cref="NotificationFactTopics.OrdersFacts"/>
/// — that test's <c>ConsumeOneAsync</c> reads its <c>.dlq</c> topic by
/// "first non-EOF message found", not by matching content, so a second
/// poison publisher on the SAME topic can steal its assertion. A
/// DIFFERENT topic (this service consumes three) makes the two tests
/// genuinely independent rather than order-dependent.
/// </summary>
[Collection(NotificationsCollection.Name)]
public sealed class LogCorrelationTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    private const string SourceTopic = NotificationFactTopics.BillingFacts;
    private const string DlqTopic = SourceTopic + ".dlq";

    [Fact]
    public async Task R58_OR7_EveryRecordProducedWhileHandlingAPoisonFactCarriesTheSameCorrelationIdAndTheSameTraceId()
    {
        await EnsureDlqTopicExistsAsync();

        using var capture = new CapturedConsole();
        Guid correlationId;

        using (capture.Redirect())
        {
            var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_notifications_logcorr_{Guid.NewGuid():N}");
            await using (var migrateDb = mssql.CreateDbContext(connectionString))
            {
                await migrateDb.Database.MigrateAsync();
            }

            var builder = NotificationsHost.CreateBuilder(
                args: [],
                configure: options =>
                {
                    options.ConnectionString = connectionString;
                    options.Kafka.BootstrapServers = kafka.BootstrapServers;
                    options.Kafka.PollTimeoutMs = 200;
                    options.FactRetry.MaxAttempts = 2;
                    options.FactRetry.BackoffMs = 100;
                    options.DeadLetter.BootstrapServers = kafka.BootstrapServers;
                });

            var sender = new NotificationConsumptionTestSupport.FakeNotificationSender();
            builder.Services.Replace(ServiceDescriptor.Singleton<INotificationSender>(sender));

            var host = builder.Build();
            await host.StartAsync();

            try
            {
                await NotificationConsumptionTestSupport.WarmUpAsync(kafka, sender, SourceTopic, TimeSpan.FromSeconds(90));

                correlationId = Guid.NewGuid();
                var poisonEnvelope = new Envelope<string>(Guid.NewGuid(), "invoice.issued.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
                var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

                await NotificationConsumptionTestSupport.PublishAsync(kafka.BootstrapServers, SourceTopic, poisonBytes);

                // Poll the CAPTURED console text itself — never the
                // broker's .dlq topic, which is NotificationDeadLetterTests'
                // own claim — until at least two records for this
                // correlationId have been written: one retry warning, one
                // final dead-letter error.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
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
                await NotificationConsumptionTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
            }
        }
    }

    private async Task EnsureDlqTopicExistsAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
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
