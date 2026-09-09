using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Contracts.Wire;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// <c>GET /orders/stream</c>'s heartbeat — acceptance bullet 2, "heartbeat
/// keeps the connection alive" — ported from #7's own separate "ping
/// heartbeat" describe block in <c>stream.integration.spec.ts</c> (#7's own
/// review finding F7: the default 15s ping interval is comfortably longer
/// than any test's own timeout, so nothing in the main describe block
/// could ever observe a `ping` — a SEPARATE app instance with a short,
/// test-only interval is required, the same reason this is its own file
/// rather than a case inside <see cref="StreamHttpTests"/>).
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class StreamHeartbeatHttpTests(NatsContainerFixture nats)
{
    private const int PingIntervalMs = 150;

    private Task<GatewayTestHost> StartAsync() => GatewayTestHost.StartAsync(options =>
    {
        options.Nats.Url = nats.Url;
        options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
        options.Mongo.Database = "otc_read_model_stream_heartbeat_it_unused";
        options.Sse.PingIntervalMs = PingIntervalMs;
    });

    private static async Task<string> LoginAsync(GatewayTestHost gateway)
    {
        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseModel>();
        return body!.AccessToken;
    }

    private sealed record LoginResponseModel(string AccessToken, string TokenType, int ExpiresIn);

    private sealed record OrderUpdatedFixture(Guid EventId, Guid OrderId, string Status, DateTimeOffset OccurredAt);

    [Fact]
    public async Task Ping_ArrivesOnTheConfiguredInterval_WithAWellFormedStreamPingBody()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);

        var request = new HttpRequestMessage(HttpMethod.Get, "/orders/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await gateway.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var reader = new SseFrameReader(await response.Content.ReadAsStreamAsync());

        var frames = await reader.CollectUntilAsync(f => f.Count(x => x.Event == "ping") >= 2, TimeSpan.FromSeconds(5));

        var pings = frames.Where(f => f.Event == "ping").ToList();
        Assert.True(pings.Count >= 2);
        foreach (var ping in pings)
        {
            var at = JsonDocument.Parse(ping.DataJson).RootElement.GetProperty("at").GetDateTimeOffset();
            Assert.True(at > DateTimeOffset.MinValue);
        }
    }

    /// <summary>
    /// Absence guard — the ONE rule in openapi.yaml's "Frame format"
    /// section with its own written-out reasoning, and the one CLAUDE.md
    /// forbids "tidying": `ping` deliberately carries no `id:` line at
    /// all, so a client's remembered `lastEventId` is never a heartbeat's.
    /// Captures a REAL content frame's id, observes a `ping` arriving
    /// STRICTLY AFTER it with no `id` at all, then reconnects with the
    /// content frame's id and confirms the stream still resumes — the
    /// exact sequence a human tester used to find #7's own regression
    /// (openapi.yaml's own citation: "Found by hand-testing after feature
    /// 26 landed").
    /// </summary>
    [Fact]
    public async Task PingFrames_CarryNoIdLine_AndReconnectingWithARealContentFramesIdStillResumes()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        await using var publisher = new NatsConnection(new NatsOpts { Url = nats.Url });
        var orderId = Guid.NewGuid();

        var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/orders/stream");
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var firstResponse = await gateway.Client.SendAsync(firstRequest, HttpCompletionOption.ResponseHeadersRead);
        var firstReader = new SseFrameReader(await firstResponse.Content.ReadAsStreamAsync());
        await firstReader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

        await publisher.PublishAsync(
            $"readmodel.order.updated.{orderId:D}",
            JsonSerializer.SerializeToUtf8Bytes(new OrderUpdatedFixture(Guid.NewGuid(), orderId, "confirmed", DateTimeOffset.UtcNow), JsonWire.Options));

        var framesSoFar = await firstReader.CollectUntilAsync(
            frames =>
            {
                var contentIndex = frames.ToList().FindIndex(f => f.Event == "order.updated");
                if (contentIndex == -1)
                {
                    return false;
                }

                return frames.Skip(contentIndex + 1).Any(f => f.Event == "ping");
            },
            TimeSpan.FromSeconds(5));

        var contentIndex = framesSoFar.ToList().FindIndex(f => f.Event == "order.updated");
        var contentFrame = framesSoFar[contentIndex];
        var pingFrame = framesSoFar.Skip(contentIndex + 1).First(f => f.Event == "ping");

        Assert.NotNull(contentFrame.Id);
        Assert.Null(pingFrame.Id);

        firstResponse.Dispose();

        var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/orders/stream");
        secondRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        secondRequest.Headers.Add("Last-Event-ID", contentFrame.Id);
        var secondResponse = await gateway.Client.SendAsync(secondRequest, HttpCompletionOption.ResponseHeadersRead);
        var secondReader = new SseFrameReader(await secondResponse.Content.ReadAsStreamAsync());

        var readyFrames = await secondReader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));
        var resumed = JsonDocument.Parse(readyFrames.Single(f => f.Event == "stream.ready").DataJson).RootElement.GetProperty("resumed").GetBoolean();

        Assert.True(resumed);
        secondResponse.Dispose();
    }

    /// <summary>
    /// The strong form of "keeps the connection alive" — not merely "a
    /// ping frame arrives", but that the connection genuinely survives an
    /// idle-timeout mechanism it would otherwise trip. This service sits
    /// behind no reverse proxy in this repository's own stack, so the
    /// idle-timeout enforcement openapi.yaml's own rationale is written
    /// against (a proxy closing a silent connection) is reproduced here at
    /// the RAW SOCKET level instead: <see cref="NetworkStream.ReadTimeout"/>
    /// is a genuine per-read idle timeout — if no bytes arrive within the
    /// window, the read throws. The window is set shorter than several
    /// multiples of the ping interval but longer than one, so surviving to
    /// the end of this test is only possible if MULTIPLE heartbeats each
    /// arrived inside their own window — deleting the ping write (see the
    /// arming table in <c>progress/impl_gateway_sse_push.md</c>) makes the
    /// very first read past `stream.ready` block until the window elapses
    /// and throw <see cref="IOException"/>, since no other traffic is
    /// published on this connection at all.
    /// </summary>
    [Fact]
    public async Task Heartbeat_KeepsTheConnectionAlive_AcrossAnIdleReadTimeoutThatWouldOtherwiseFireWithoutIt()
    {
        await using var gateway = await StartAsync();
        var token = await LoginAsync(gateway);
        var baseUri = gateway.Client.BaseAddress!;

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(baseUri.Host, baseUri.Port);
        using var stream = tcpClient.GetStream();

        // > one ping interval, so a single heartbeat satisfies each
        // window; < several multiples of it, so surviving several windows
        // genuinely requires several heartbeats, not one lucky write.
        stream.ReadTimeout = PingIntervalMs * 4;

        var requestBytes = Encoding.ASCII.GetBytes(
            $"GET /orders/stream HTTP/1.1\r\nHost: {baseUri.Host}\r\nAuthorization: Bearer {token}\r\nConnection: keep-alive\r\n\r\n");
        await stream.WriteAsync(requestBytes);

        var buffer = new byte[8192];
        var accumulated = new StringBuilder();
        var successfulReads = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(PingIntervalMs * 4 * 6); // survive across ~6 read windows

        while (DateTime.UtcNow < deadline)
        {
            // NetworkStream.Read is the synchronous, ReadTimeout-honouring
            // member — ReadAsync does not observe ReadTimeout the same way
            // (it honours a CancellationToken instead), so this MUST be
            // the synchronous call to exercise the real per-read idle
            // timeout this test is about.
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.True(read > 0, "the connection closed (EOF) before the deadline.");
            accumulated.Append(Encoding.ASCII.GetString(buffer, 0, read));
            successfulReads++;
        }

        Assert.True(successfulReads >= 3, $"expected multiple successful reads inside the idle window, got {successfulReads}.");
        var text = accumulated.ToString();
        Assert.Contains("event: stream.ready", text, StringComparison.Ordinal);
        Assert.Contains("event: ping", text, StringComparison.Ordinal);
    }
}
