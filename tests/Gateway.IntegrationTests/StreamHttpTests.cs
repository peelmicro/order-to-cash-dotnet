using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using OrderToCash.Contracts.Wire;
using OrderToCash.Gateway.Application.Stream;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// <c>GET /orders/stream</c> (R55, feature <c>gateway_sse_push</c>) —
/// ported from #7's <c>stream.integration.spec.ts</c> "Group D" describe
/// block. Real NATS (<see cref="NatsContainerFixture"/>), real Kestrel
/// (<see cref="GatewayTestHost"/>); MongoDB is left DELIBERATELY
/// unreachable (a dummy connection string) because no test in this file
/// exercises the read model — the stream is fed exclusively by
/// <c>readmodel.order.updated.&lt;orderId&gt;</c>/
/// <c>readmodel.timeline.appended.&lt;orderId&gt;</c>, published directly
/// here with a SEPARATE NATS connection, exactly the subjects the
/// projector's own <c>NatsUpdateSignalPublisher</c> uses (this gateway
/// never talks to the projector directly; it only consumes the signal it
/// publishes). <see cref="StreamProjectorEndToEndTests"/> is the one place
/// in this feature that boots the REAL projector and walks the whole chain.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class StreamHttpTests(NatsContainerFixture nats)
{
    private Task<GatewayTestHost> StartAsync() => GatewayTestHost.StartAsync(options =>
    {
        options.Nats.Url = nats.Url;
        options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
        options.Mongo.Database = "otc_read_model_stream_it_unused";
    });

    private static async Task<string> LoginAsync(GatewayTestHost gateway)
    {
        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseModel>();
        return body!.AccessToken;
    }

    private sealed record LoginResponseModel(string AccessToken, string TokenType, int ExpiresIn);

    private static async Task<(HttpResponseMessage Response, SseFrameReader Reader)> OpenSseConnectionAsync(
        HttpClient client, string token, string query = "", string? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/stream{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = await response.Content.ReadAsStreamAsync();
        return (response, new SseFrameReader(stream));
    }

    private sealed record OrderUpdatedFixture(Guid EventId, Guid OrderId, string Status, DateTimeOffset OccurredAt);

    private sealed record TimelineAppendedFixture(Guid EventId, Guid CausationId, Guid OrderId, string EventType, DateTimeOffset OccurredAt, string Summary);

    private static byte[] EncodeOrderUpdated(Guid orderId, string status, Guid? eventId = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new OrderUpdatedFixture(eventId ?? Guid.NewGuid(), orderId, status, DateTimeOffset.UtcNow), JsonWire.Options);

    private static byte[] EncodeTimelineAppended(Guid orderId, string eventType, Guid? eventId = null) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new TimelineAppendedFixture(eventId ?? Guid.NewGuid(), Guid.NewGuid(), orderId, eventType, DateTimeOffset.UtcNow, "test summary"),
            JsonWire.Options);

    [Fact]
    public async Task Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var orderId = Guid.NewGuid();

        var (response, reader) = await OpenSseConnectionAsync(gateway.Client, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.Contains("keep-alive", response.Headers.Connection);

        var readyFrames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));
        var ready = readyFrames.Single(f => f.Event == "stream.ready");
        Assert.False(JsonDocument.Parse(ready.DataJson).RootElement.GetProperty("resumed").GetBoolean());

        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", EncodeOrderUpdated(orderId, "confirmed"));

        var frames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));
        var updateFrame = frames.Single(f => f.Event == "order.updated");
        Assert.Equal(orderId, JsonDocument.Parse(updateFrame.DataJson).RootElement.GetProperty("orderId").GetGuid());
        Assert.NotNull(updateFrame.Id);
    }

    [Fact]
    public async Task OrderIdFilter_AClientSubscribedToOneOrder_NeverReceivesAnotherOrdersFrames()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var watchedOrderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();

        var (_, reader) = await OpenSseConnectionAsync(gateway.Client, token, $"?orderId={watchedOrderId}");
        await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        await publisher.PublishAsync($"readmodel.order.updated.{otherOrderId:D}", EncodeOrderUpdated(otherOrderId, "confirmed"));
        await publisher.PublishAsync($"readmodel.order.updated.{watchedOrderId:D}", EncodeOrderUpdated(watchedOrderId, "confirmed"));

        var frames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));

        // Give a stray "otherOrderId" frame a fair chance to arrive too,
        // before asserting it never did — CollectUntilAsync only proves the
        // WATCHED frame arrived, not that nothing else ever will.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var updateFrames = frames.Where(f => f.Event == "order.updated").ToList();

        Assert.Single(updateFrames);
        Assert.Equal(watchedOrderId, JsonDocument.Parse(updateFrames[0].DataJson).RootElement.GetProperty("orderId").GetGuid());
    }

    /// <summary>R55 reconnection — a known Last-Event-ID replays every frame missed since, resumed:true.</summary>
    [Fact]
    public async Task Reconnect_AKnownLastEventId_ReplaysEveryFrameMissedSince_ResumedTrue()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var orderId = Guid.NewGuid();

        var (firstResponse, firstReader) = await OpenSseConnectionAsync(gateway.Client, token);
        await firstReader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", EncodeOrderUpdated(orderId, "stock_reserved"));
        var firstFrames = await firstReader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));
        var cursorAfterFirstFrame = firstFrames.Single(f => f.Event == "order.updated").Id!;
        firstResponse.Dispose();

        // The client is disconnected while this fact is published — this is
        // exactly what the bounded replay buffer exists to recover.
        await publisher.PublishAsync($"readmodel.timeline.appended.{orderId:D}", EncodeTimelineAppended(orderId, "stock.reserved.v1"));
        await publisher.PingAsync();

        // Retry the reconnect on TERMINAL evidence (stream.ready's own
        // "resumed" flag), not a fixed sleep — the app's OWN internal NATS
        // subscription (started once at boot, independent of any SSE
        // client) is what actually captures the fact into the replay
        // buffer, and `PingAsync` only proves the publish reached the NATS
        // server, not that this app's subscription loop has processed it
        // yet.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        SseFrame ready;
        SseFrameReader secondReader;
        while (true)
        {
            var (_, reader) = await OpenSseConnectionAsync(gateway.Client, token, lastEventId: cursorAfterFirstFrame);
            var readyFrames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(3));
            ready = readyFrames.Single(f => f.Event == "stream.ready");
            secondReader = reader;
            if (JsonDocument.Parse(ready.DataJson).RootElement.GetProperty("resumed").GetBoolean())
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("stream.ready never reported resumed:true within 10s.");
            }

            await Task.Delay(50);
        }

        Assert.True(JsonDocument.Parse(ready.DataJson).RootElement.GetProperty("resumed").GetBoolean());
        Assert.Equal(cursorAfterFirstFrame, JsonDocument.Parse(ready.DataJson).RootElement.GetProperty("cursor").GetString());

        var frames = await secondReader.CollectUntilAsync(f => f.Any(x => x.Event == "timeline.appended"), TimeSpan.FromSeconds(5));
        var timelineFrame = frames.Single(f => f.Event == "timeline.appended");
        Assert.Equal(orderId, JsonDocument.Parse(timelineFrame.DataJson).RootElement.GetProperty("orderId").GetGuid());
    }

    [Fact]
    public async Task Reconnect_AnUnknownOrAgedOutLastEventId_AnswersStreamReadyResumedFalse_NeverAnError()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);

        var (response, reader) = await OpenSseConnectionAsync(gateway.Client, token, lastEventId: "not-a-real-cursor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var frames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));
        var ready = frames.Single(f => f.Event == "stream.ready");
        Assert.False(JsonDocument.Parse(ready.DataJson).RootElement.GetProperty("resumed").GetBoolean());
    }

    /// <summary>
    /// Acceptance bullet 1, "client reconnect resumes without duplicates" —
    /// read literally against openapi.yaml's own admission that delivery is
    /// AT-LEAST-ONCE (a frame may repeat after a reconnect; clients
    /// deduplicate on `eventId`). What IS a transport-level guarantee, and
    /// what this test proves: resuming from a given cursor returns
    /// strictly the frames AFTER that cursor, never the cursor's own frame
    /// again — "resumes after", never "resumes at-or-before".
    /// </summary>
    [Fact]
    public async Task Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var orderId = Guid.NewGuid();
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();

        var (firstResponse, firstReader) = await OpenSseConnectionAsync(gateway.Client, token, $"?orderId={orderId}");
        await firstReader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", EncodeOrderUpdated(orderId, "stock_reserved", firstEventId));
        var firstFrames = await firstReader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));
        var cursorOfReceivedFrame = firstFrames.Single(f => f.Event == "order.updated").Id!;
        firstResponse.Dispose();

        // Disconnected here. One more fact arrives — legitimately what the
        // reconnect is supposed to replay; the assertion below is that it
        // replays ONLY this one, never the one already delivered above.
        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", EncodeOrderUpdated(orderId, "credit_approved", secondEventId));
        await publisher.PingAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        SseFrameReader secondReader;
        while (true)
        {
            var (_, reader) = await OpenSseConnectionAsync(gateway.Client, token, $"?orderId={orderId}", cursorOfReceivedFrame);
            var readyFrames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(3));
            var resumed = JsonDocument.Parse(readyFrames.Single(f => f.Event == "stream.ready").DataJson).RootElement.GetProperty("resumed").GetBoolean();
            secondReader = reader;
            if (resumed)
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("R55 \"without duplicates\": stream.ready never reported resumed:true within 10s.");
            }

            await Task.Delay(50);
        }

        var frames = await secondReader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));
        // Give a duplicate delivery of the ALREADY-received frame a fair
        // chance to arrive too, before asserting it never did.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var updateFrames = frames.Where(f => f.Event == "order.updated").ToList();

        Assert.DoesNotContain(updateFrames, f => f.Id == cursorOfReceivedFrame);
        Assert.DoesNotContain(updateFrames, f => JsonDocument.Parse(f.DataJson).RootElement.GetProperty("eventId").GetGuid() == firstEventId);
        Assert.Single(updateFrames);
        Assert.Equal(secondEventId, JsonDocument.Parse(updateFrames[0].DataJson).RootElement.GetProperty("eventId").GetGuid());
    }

    /// <summary>
    /// The ported-idiom ledger's row 6 (progress/impl_gateway_rest_auth.md)
    /// licenses "literal beats parameter at the same registration
    /// position", probed only over a throwaway `/probe/*` pair
    /// (<c>RoutePrecedenceTests</c>). This proves it holds for the REAL
    /// production route: if the parameter route `/orders/{id}` ever
    /// swallowed `stream` as an `id`, <c>RequestParsing.ParseId</c> would
    /// reject "stream" as not a valid GUID and answer 400 — this asserts
    /// 200 `text/event-stream` instead.
    /// </summary>
    [Fact]
    public async Task GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);

        var (response, _) = await OpenSseConnectionAsync(gateway.Client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>
    /// A malformed frame on the wire is logged and skipped, never brings
    /// the subscription down — ported from #7's own
    /// <c>nats-stream-signal.adapter.spec.ts</c>. Publishes a frame that is
    /// not even syntactically valid JSON, then a well-formed one on the
    /// SAME subject, and proves the well-formed one still arrives.
    /// </summary>
    [Fact]
    public async Task AMalformedSignalFrame_IsSkipped_WithoutBreakingConsumptionOfTheNextWellFormedFrame()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var orderId = Guid.NewGuid();

        var (_, reader) = await OpenSseConnectionAsync(gateway.Client, token, $"?orderId={orderId}");
        await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", "not json"u8.ToArray());
        await publisher.PublishAsync($"readmodel.order.updated.{orderId:D}", EncodeOrderUpdated(orderId, "confirmed"));

        var frames = await reader.CollectUntilAsync(f => f.Any(x => x.Event == "order.updated"), TimeSpan.FromSeconds(5));
        var updateFrames = frames.Where(f => f.Event == "order.updated").ToList();

        Assert.Single(updateFrames);
        Assert.Equal(orderId, JsonDocument.Parse(updateFrames[0].DataJson).RootElement.GetProperty("orderId").GetGuid());
    }

    /// <summary>
    /// The ported-idiom ledger's row 10 (<c>progress/impl_gateway_sse_push.md</c>):
    /// #7's teardown is Express/Nest's engine-supplied <c>res.on('close', ...)</c>
    /// (<c>stream.controller.ts:80-83</c>), which here is HAND-WRITTEN —
    /// <c>HttpContext.RequestAborted</c> unwinding the read/ping loop plus a
    /// <c>finally { hub.Unsubscribe(subscriptionId); }</c>
    /// (<c>StreamEndpoints.cs:143-157</c>). This is the ONE guard for that
    /// hand-written path, and it reaches it through a REAL client disconnect
    /// over REAL Kestrel — never by calling <c>StreamHub.Unsubscribe</c>
    /// directly, which would prove nothing about whether the endpoint's own
    /// <c>finally</c> block still calls it. Deleting
    /// <c>hub.Unsubscribe(subscriptionId)</c> from that <c>finally</c> leaves
    /// this test's second wait spinning until its own deadline throws —
    /// see the arming table.
    /// </summary>
    [Fact]
    public async Task Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        var hub = gateway.Services.GetRequiredService<StreamHub>();
        Assert.Equal(0, hub.SubscriberCount);

        var (response, reader) = await OpenSseConnectionAsync(gateway.Client, token);
        await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        // By the time `stream.ready`'s bytes have reached the client, the
        // endpoint has already called `hub.Subscribe()` — it happens
        // strictly before the first `WriteFrameAsync` call in
        // `StreamEndpoints.StreamAsync` — so this is a direct read, never a
        // poll.
        Assert.Equal(1, hub.SubscriberCount);

        response.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (hub.SubscriberCount != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(0, hub.SubscriberCount);
    }
}
