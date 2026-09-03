using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OI7 — asserted on the constructed <see cref="Confluent.Kafka.ProducerConfig"/>,
/// never by mocking a broker (design.md §5.3, §9.1).
/// </summary>
public sealed class KafkaFactPublisherConfigTests
{
    [Fact]
    public void OI7_Producer_IsConfiguredSoAnInternalRetryCanNeitherReorderNorDuplicateAPartitionsRecords()
    {
        var config = KafkaFactPublisher.BuildProducerConfig(new KafkaOptions { BootstrapServers = "localhost:9092", ClientId = "otc-orders" });

        Assert.True(config.EnableIdempotence);
        Assert.Equal(Confluent.Kafka.Acks.All, config.Acks);
        Assert.Equal(int.MaxValue, config.MessageSendMaxRetries);

        // librdkafka's idempotent producer preserves per-partition order at
        // up to five in-flight requests, which is why #8's number (5)
        // differs from #7's kafkajs client, which pins
        // maxInFlightRequests = 1 to get the same guarantee.
        Assert.Equal(5, config.MaxInFlight);
    }
}
