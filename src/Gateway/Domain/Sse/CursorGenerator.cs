namespace OrderToCash.Gateway.Domain.Sse;

/// <summary>
/// The SSE frame <c>id:</c> cursor (openapi.yaml <c>/orders/stream</c>'s
/// "Frame format" section: <c>id: 1755511234567-17</c>) — an opaque,
/// monotonically increasing cursor. Epoch-millisecond prefix (so a cursor
/// is roughly time-ordered for a human reading raw frames) plus a
/// per-instance sequence suffix (so two frames minted within the same
/// millisecond never collide).
/// </summary>
/// <remarks>
/// Ported from #7's <c>apps/gateway/src/domain/sse/cursor.ts</c>
/// (<c>CursorGenerator</c>) — pure, takes a clock delegate rather than
/// <see cref="DateTimeOffset.UtcNow"/> directly, the SAME
/// <c>Func&lt;DateTimeOffset&gt;</c> shape <c>IssuedOrderWindow</c> already
/// establishes in this service, so it is deterministic under test.
///
/// One structural adaptation, recorded in the ported-idiom ledger
/// (<c>progress/impl_gateway_sse_push.md</c>): #7's own sequence counter is
/// a plain <c>this.sequence += 1</c> because Node's event loop is
/// single-threaded — two calls to <c>next()</c> can never genuinely race
/// there. This hub is fed by TWO concurrent NATS subscription loops
/// (<c>order.updated</c>, <c>timeline.appended</c> —
/// <c>NatsStreamSignalSubscriber</c>) running on the .NET thread pool, so
/// two calls to <see cref="Next"/> CAN happen at the same instant on
/// different threads; <see cref="Interlocked.Increment(ref long)"/>
/// replaces the plain increment so uniqueness holds under genuine
/// concurrency, not merely under a single-threaded assumption ported
/// unchanged from a runtime where it happened to be safe.
/// </remarks>
public sealed class CursorGenerator(Func<DateTimeOffset> clock)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private long _sequence;

    public string Next()
    {
        var sequence = Interlocked.Increment(ref _sequence);
        return $"{_clock().ToUnixTimeMilliseconds()}-{sequence}";
    }
}
