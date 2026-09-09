namespace OrderToCash.Gateway.Domain.Sse;

/// <summary>
/// The stream's "bounded replay buffer" (openapi.yaml <c>/orders/stream</c>
/// reconnection section, R55): a fixed-capacity, oldest-evicted-first
/// history of every frame emitted, keyed by its opaque cursor.
/// </summary>
/// <remarks>
/// Ported from #7's <c>apps/gateway/src/domain/sse/replay-buffer.ts</c>
/// (<c>ReplayBuffer&lt;T&gt;</c>). The two honest limitations openapi.yaml
/// states are both structural consequences of this shape, not
/// special-cased: (1) a cursor older than the oldest surviving entry is
/// indistinguishable from a cursor that never existed — <see cref="ReplayAfter"/>
/// returns <c>Resumed: false</c> for both, and the caller
/// (<c>StreamEndpoints</c>) turns that into <c>stream.ready { resumed:
/// false }</c> plus the documented "go re-fetch the read model" guidance.
/// (2) delivery is at-least-once, because a frame already relayed to a slow
/// consumer can still be re-sent on reconnect if the consumer's own
/// <c>Last-Event-ID</c> lagged behind what it actually received — this
/// buffer makes no promise about exactly-once, only "give me everything
/// after this cursor, if I still have it".
///
/// One structural adaptation from #7's own (deliberately not thread-safe —
/// Node's event loop serialises every call) version: <see cref="Push"/> is
/// invoked from TWO concurrent NATS subscription loops
/// (<c>NatsStreamSignalSubscriber</c>) and <see cref="ReplayAfter"/> is
/// invoked from every SSE connection's own Kestrel request thread — all
/// genuinely concurrent in this runtime — so every member here is guarded
/// by one <see langword="lock"/>, the SAME <c>lock (field)</c> shape
/// <c>IssuedOrderWindow</c> already establishes in this service.
/// </remarks>
public sealed class ReplayBuffer<T>
{
    private readonly int _capacity;
    private readonly LinkedList<(string Cursor, T Value)> _entries = new();

    public ReplayBuffer(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "ReplayBuffer: capacity must be at least 1.");
        }

        _capacity = capacity;
    }

    public void Push(string cursor, T value)
    {
        lock (_entries)
        {
            _entries.AddLast((cursor, value));
            while (_entries.Count > _capacity)
            {
                _entries.RemoveFirst();
            }
        }
    }

    /// <summary><paramref name="cursor"/> is <see langword="null"/> for a fresh connection (no <c>Last-Event-ID</c> header at all) — never a resume, <c>Resumed: false</c>, nothing missed by definition (there is nothing to have missed).</summary>
    public ReplayResult<T> ReplayAfter(string? cursor)
    {
        lock (_entries)
        {
            if (cursor is null)
            {
                return new ReplayResult<T>(false, []);
            }

            var node = _entries.First;
            var missed = new List<T>();
            var found = false;

            while (node is not null)
            {
                if (found)
                {
                    missed.Add(node.Value.Value);
                }
                else if (node.Value.Cursor == cursor)
                {
                    found = true;
                }

                node = node.Next;
            }

            return found ? new ReplayResult<T>(true, missed) : new ReplayResult<T>(false, []);
        }
    }

    public int Size
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }
}
