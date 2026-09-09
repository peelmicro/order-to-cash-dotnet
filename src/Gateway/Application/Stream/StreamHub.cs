using System.Collections.Concurrent;
using System.Threading.Channels;
using OrderToCash.Gateway.Domain.Sse;

namespace OrderToCash.Gateway.Application.Stream;

/// <summary>
/// The live hub behind <c>GET /orders/stream</c> (R55): one process-wide
/// bounded replay buffer (<see cref="ReplayBuffer{T}"/>) plus a fan-out to
/// every open SSE connection. One singleton per Gateway process — fed by
/// <c>NatsStreamSignalSubscriber</c> (Infrastructure), read by
/// <c>StreamEndpoints</c> (Presentation). Deliberately NOT a command or
/// query handler: it has no <c>ICommandHandler</c>/<c>IQueryHandler</c> of
/// its own, the same "neither" #7's own header comment on
/// <c>application/stream-hub.ts</c> establishes for its NestJS CQRS
/// equivalent.
/// </summary>
/// <remarks>
/// Ported from #7's <c>application/stream-hub.ts</c> (<c>StreamHub</c>).
/// One structural adaptation, recorded in the ported-idiom ledger
/// (<c>progress/impl_gateway_sse_push.md</c>): #7 fans a published frame
/// out through one RxJS <c>Subject</c> every open connection subscribes to.
/// There is no BCL equivalent of a multicast <c>Subject</c>, so this hub
/// keeps one bounded <see cref="Channel{T}"/> PER live SSE connection
/// instead (<see cref="Subscribe"/>/<see cref="Unsubscribe"/>),
/// broadcasting a published frame by writing it into every subscriber's own
/// channel. <c>System.Threading.Channels</c> is part of the shared
/// framework since .NET Core 3.0 — no new NuGet package; it is the SAME
/// type <c>ChannelSagaCommandSignal</c> (<c>src/Orders/Infrastructure/Saga/</c>)
/// already uses in this repository for its own single-consumer case — this
/// is the multi-consumer fan-out case.
/// </remarks>
public sealed class StreamHub
{
    private const int SubscriberChannelCapacity = 256;

    private readonly CursorGenerator _cursor;
    private readonly ReplayBuffer<StreamFrame> _buffer;
    private readonly ConcurrentDictionary<Guid, Channel<StreamFrame>> _subscribers = new();

    public StreamHub(Func<DateTimeOffset> clock, int capacity)
    {
        _cursor = new CursorGenerator(clock);
        _buffer = new ReplayBuffer<StreamFrame>(capacity);
    }

    /// <summary>Mints a fresh, buffer-pushed cursor, fans the frame out to every live subscriber, and returns it.</summary>
    public StreamFrame Publish(string eventType, Guid orderId, string dataJson)
    {
        var frame = new StreamFrame(_cursor.Next(), eventType, orderId, dataJson);
        _buffer.Push(frame.Cursor, frame);

        foreach (var channel in _subscribers.Values)
        {
            // Bounded + DropOldest: a slow or stalled SSE consumer must
            // never block a live publish from a NATS subscription loop
            // that is feeding every OTHER connection too. DropOldest
            // (rather than ChannelSagaCommandSignal's own DropWrite) is the
            // right choice for THIS queue specifically: a stalled reader
            // should catch up on the most RECENT frames once it resumes
            // reading, not keep ones already stale by the time it does.
            channel.Writer.TryWrite(frame);
        }

        return frame;
    }

    public ReplayResult<StreamFrame> ReplayAfter(string? cursor) => _buffer.ReplayAfter(cursor);

    /// <summary>
    /// A fresh cursor NOT pushed to the replay buffer — used for the
    /// `stream.ready`/`ping` frames, which name a moment in time but carry
    /// no re-playable content of their own. See openapi.yaml's own
    /// rationale for why `ping` deliberately carries no `id:` line at all —
    /// `StreamEndpoints`.
    /// </summary>
    public string MintCursor() => _cursor.Next();

    /// <summary>Registers a new live subscriber and returns its id (for <see cref="Unsubscribe"/>) and a reader every <see cref="Publish"/> call after this point writes into.</summary>
    public (Guid SubscriptionId, ChannelReader<StreamFrame> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<StreamFrame>(new BoundedChannelOptions(SubscriberChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscriptionId = Guid.NewGuid();
        _subscribers[subscriptionId] = channel;
        return (subscriptionId, channel.Reader);
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    /// <summary>Test/diagnostic seam only — the number of currently live subscribers.</summary>
    public int SubscriberCount => _subscribers.Count;
}
