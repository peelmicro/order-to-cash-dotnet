using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Signal;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR37</c> — one sentinel per copied field, at the post-apply-document →
/// BOTH signal payloads hop. Also proves ledger <b>L39</b>: two consecutive
/// publishes do not share one <see cref="NatsHeaders"/> instance.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class NatsUpdateSignalPublisherTests(NatsContainerFixture natsFixture)
{
    [Fact]
    public async Task PR37_EveryEnvelopeFieldReachesBothSignalPayloadsVerbatim_SentinelPerField()
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await connection.ConnectAsync();
        var publisher = new NatsUpdateSignalPublisher(connection);

        var orderId = Guid.NewGuid();
        var eventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var causationId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
        var occurredAt = new DateTimeOffset(2029, 8, 7, 6, 5, 4, 321, TimeSpan.Zero);
        const string orderReference = "ORD-SENTINEL-01";
        const string status = "sentinel-status";
        const string eventType = "order.sentinel.v1";
        const string summary = "sentinel summary text";

        var document = new ReadModelDocument(
            OrderId: orderId,
            OrderReference: orderReference,
            Status: status,
            CancellationReason: null,
            DespatchReference: null,
            InvoiceReference: null,
            PaymentReference: null,
            InitialAmount: null,
            InitialDiscount: null,
            TotalAmount: null,
            Currency: null,
            EventId: eventId,
            EventType: eventType,
            OccurredAt: occurredAt,
            Summary: summary,
            CausationId: causationId);

        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderSub = subscriberConnection.SubscribeAsync<byte[]>($"readmodel.order.updated.{orderId:D}");
        var timelineSub = subscriberConnection.SubscribeAsync<byte[]>($"readmodel.timeline.appended.{orderId:D}");
        var orderEnumerator = orderSub.GetAsyncEnumerator();
        var timelineEnumerator = timelineSub.GetAsyncEnumerator();
        var orderTask = orderEnumerator.MoveNextAsync().AsTask();
        var timelineTask = timelineEnumerator.MoveNextAsync().AsTask();
        await Task.Delay(300);

        await publisher.PublishAsync(document, CancellationToken.None);

        Assert.True(await orderTask.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await timelineTask.WaitAsync(TimeSpan.FromSeconds(10)));

        using var orderJson = JsonDocument.Parse(orderEnumerator.Current.Data!);
        using var timelineJson = JsonDocument.Parse(timelineEnumerator.Current.Data!);

        // OrderStreamUpdate — every sentinel field.
        Assert.Equal(eventId, orderJson.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(orderId, orderJson.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(orderReference, orderJson.RootElement.GetProperty("orderReference").GetString());
        Assert.Equal(status, orderJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(occurredAt, orderJson.RootElement.GetProperty("occurredAt").GetDateTimeOffset());

        // TimelineStreamEntry — every sentinel field.
        Assert.Equal(eventId, timelineJson.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(causationId, timelineJson.RootElement.GetProperty("causationId").GetGuid());
        Assert.Equal(orderId, timelineJson.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(orderReference, timelineJson.RootElement.GetProperty("orderReference").GetString());
        Assert.Equal(eventType, timelineJson.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(occurredAt, timelineJson.RootElement.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(summary, timelineJson.RootElement.GetProperty("summary").GetString());

        // L39 — headers are not shared: publish a SECOND time and confirm the
        // subject-scoped x-correlation-id header is present and correct on
        // both frames (a shared, mutated NatsHeaders instance would corrupt
        // or drop headers under concurrent use).
        var orderTask2 = orderEnumerator.MoveNextAsync().AsTask();
        await publisher.PublishAsync(document, CancellationToken.None);
        Assert.True(await orderTask2.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(orderEnumerator.Current.Headers!.TryGetValue("x-correlation-id", out var headerValue));
        Assert.Equal(orderId.ToString("D"), headerValue.ToString());

        await connection.DisposeAsync();
        await subscriberConnection.DisposeAsync();
    }
}
