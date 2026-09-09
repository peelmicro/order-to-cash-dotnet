using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Wire;
using OrderToCash.Gateway.Application.Stream;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Presentation.Dto;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary>
/// <c>GET /orders/stream</c> — openapi.yaml <c>stream</c> tag, R55. Writes
/// the raw SSE response body directly (never a framework SSE helper —
/// ASP.NET Core Minimal APIs have none) so this endpoint has EXACT control
/// over both required response headers (<c>Cache-Control: no-cache</c>,
/// <c>Connection: keep-alive</c> — openapi.yaml's own <c>const</c>s) and
/// the precise <c>id:</c>/<c>event:</c>/<c>data:</c> frame shape the
/// contract's own "Frame format" section specifies verbatim. Ported from
/// #7's <c>presentation/stream.controller.ts</c> (<c>StreamController</c>),
/// which bypasses NestJS's own <c>@Sse()</c> decorator for the identical
/// reason (its comment: <c>@Sse()</c> sends a longer
/// <c>Cache-Control</c> than the contract's own <c>const</c>).
///
/// Mapped as its own literal route, registered AFTER
/// <c>OrdersEndpoints.MapOrdersEndpoints</c> (which maps the parameter
/// route <c>GET /orders/{id}</c>) — the SAME registration order
/// <c>RoutePrecedenceTests</c> already probed and the ported-idiom ledger's
/// row 6 already licenses: "ASP.NET Core Minimal API routing is NOT
/// order-sensitive: a literal route segment always outranks a
/// route-parameter segment at the same position, regardless of
/// <c>Map*</c> call order."
/// <c>StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute</c>
/// proves this holds for THIS real production route, not only the probe
/// route <c>RoutePrecedenceTests</c> built.
/// </summary>
public static class StreamEndpoints
{
    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/stream", StreamAsync);

        return app;
    }

    private static async Task StreamAsync(
        HttpContext context,
        StreamHub hub,
        IOptions<GatewaySseOptions> sseOptions,
        CancellationToken cancellationToken)
    {
        Guid? orderIdFilter = null;
        var rawOrderId = context.Request.Query["orderId"].FirstOrDefault();
        if (!string.IsNullOrEmpty(rawOrderId))
        {
            orderIdFilter = RequestParsing.ParseId("orderId", rawOrderId);
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";

        var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();

        // Subscribed BEFORE the replay snapshot is read — deliberately, to
        // close the one race #7's own controller does not: if the buffer
        // is read first and only THEN subscribed, a frame published in
        // between is neither in the snapshot nor delivered live, and is
        // lost outright. Subscribing first means the worst case is a
        // harmless DUPLICATE (also delivered live, also present in the
        // replay snapshot) — exactly the at-least-once delivery
        // openapi.yaml already documents as a client-side dedup
        // responsibility (R51/`eventId`), never a silent drop.
        var (subscriptionId, reader) = hub.Subscribe();
        try
        {
            // R55's reconnection contract: resume from the bounded buffer
            // when the client's Last-Event-ID is still in it; resumed:false
            // (never an error) when it is not, or when there was no
            // Last-Event-ID at all (a fresh connection).
            var replay = hub.ReplayAfter(lastEventId);
            var readyCursor = lastEventId ?? hub.MintCursor();

            await WriteFrameAsync(
                context.Response,
                id: null,
                "stream.ready",
                JsonSerializer.Serialize(new StreamReadyResponseDto(readyCursor, replay.Resumed, orderIdFilter), JsonWire.Options),
                cancellationToken).ConfigureAwait(false);

            foreach (var frame in replay.Missed)
            {
                if (Matches(frame, orderIdFilter))
                {
                    await WriteFrameAsync(context.Response, frame.Cursor, frame.EventType, frame.DataJson, cancellationToken).ConfigureAwait(false);
                }
            }

            using var pingTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(sseOptions.Value.PingIntervalMs));
            var readTask = reader.ReadAsync(cancellationToken).AsTask();
            var pingTask = pingTimer.WaitForNextTickAsync(cancellationToken).AsTask();

            while (true)
            {
                var completed = await Task.WhenAny(readTask, pingTask).ConfigureAwait(false);

                if (completed == readTask)
                {
                    var frame = await readTask.ConfigureAwait(false);
                    if (Matches(frame, orderIdFilter))
                    {
                        await WriteFrameAsync(context.Response, frame.Cursor, frame.EventType, frame.DataJson, cancellationToken).ConfigureAwait(false);
                    }

                    readTask = reader.ReadAsync(cancellationToken).AsTask();
                }
                else
                {
                    var ticked = await pingTask.ConfigureAwait(false);
                    if (!ticked)
                    {
                        break;
                    }

                    // Consumed for its side effect on the cursor sequence
                    // only — never pushed into the replay buffer, and
                    // `ping` carries no `id:` line at all. See
                    // WriteFrameAsync's own remarks for why.
                    hub.MintCursor();
                    await WriteFrameAsync(
                        context.Response,
                        id: null,
                        "ping",
                        JsonSerializer.Serialize(new StreamPingResponseDto(DateTimeOffset.UtcNow), JsonWire.Options),
                        cancellationToken).ConfigureAwait(false);

                    pingTask = pingTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected (or the app is shutting down) — a
            // 200 response has already started writing bytes by the time
            // this can happen, so there is nothing left to translate into
            // a Problem response; rethrowing here would reach
            // ProblemJsonMiddleware AFTER headers are already sent, which
            // throws its own InvalidOperationException ("response has
            // already started") and would bury this, the real, expected
            // outcome of an SSE client simply going away.
        }
        finally
        {
            hub.Unsubscribe(subscriptionId);
        }
    }

    private static bool Matches(StreamFrame frame, Guid? orderIdFilter) => orderIdFilter is null || frame.OrderId == orderIdFilter;

    /// <summary>
    /// <paramref name="id"/> is <see langword="null"/> for `stream.ready`/`ping`
    /// — deliberately: per the SSE spec, ANY dispatched event carrying an
    /// `id:` line updates a browser `EventSource`'s `lastEventId`,
    /// heartbeat or not. `ping` fires on a fixed interval and real content
    /// fires rarely, so if `ping` carried an id, a client's remembered
    /// resume point would almost always be a heartbeat's — one the replay
    /// buffer never stores (`StreamHub.MintCursor`'s own remarks) — making
    /// reconnection answer `resumed: false` on nearly every disconnect
    /// instead of only when content was genuinely missed. See
    /// openapi.yaml's "Frame format" section, which states this rule and
    /// its reasoning in full.
    /// </summary>
    private static async Task WriteFrameAsync(HttpResponse response, string? id, string eventType, string dataJson, CancellationToken cancellationToken)
    {
        var frame = new StringBuilder();
        if (id is not null)
        {
            frame.Append("id: ").Append(id).Append('\n');
        }

        frame.Append("event: ").Append(eventType).Append('\n');
        frame.Append("data: ").Append(dataJson).Append("\n\n");

        await response.WriteAsync(frame.ToString(), cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
