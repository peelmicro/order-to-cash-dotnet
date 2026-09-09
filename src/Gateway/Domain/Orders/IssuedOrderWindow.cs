namespace OrderToCash.Gateway.Domain.Orders;

/// <summary>
/// A bounded, TTL'd memory of order ids THIS gateway process has recently
/// handed out from <c>POST /orders</c> — what <c>GET /orders/{id}</c> uses
/// to distinguish R55's "projection pending" (202) from openapi.yaml's own
/// "a genuine 404 means the identifier is unknown to the system".
/// </summary>
/// <remarks>
/// Ported from #7's <c>apps/gateway/src/domain/orders/issued-order-window.ts</c>
/// (<c>IssuedOrderWindow</c>) — a review finding (#7's F3), not a first
/// draft: before it existed, EVERY read-model miss answered 202, making
/// openapi.yaml's documented 404 unreachable. R55's own clause is scoped to
/// "an order identifier that the caller has just been given", and the
/// gateway is exactly the component that hands the id out
/// (<c>POST /orders</c> returns it), so "an id the caller has just been
/// given" is, by construction, an id THIS PROCESS issued recently — no new
/// RPC subject is needed to answer this honestly. Oldest-evicted-first once
/// <see cref="_capacity"/> is exceeded, mirroring the SSE replay buffer's
/// own bounded-memory discipline (feature <c>gateway_sse_push</c>) —
/// deliberately NOT a permanent memory of every order ever placed, only of
/// ones recent enough that "still catching up to the read model" is a
/// credible explanation for their absence. Pure — no ASP.NET Core, no
/// <see cref="DateTime.UtcNow"/> directly (a <c>nowUtc</c> delegate instead)
/// — so it is unit-testable without a clock double and the ESLint-
/// equivalent domain-purity architecture tests leave it alone.
/// </remarks>
public sealed class IssuedOrderWindow
{
    private readonly Dictionary<Guid, DateTimeOffset> _issuedAt = new();
    private readonly Func<DateTimeOffset> _nowUtc;
    private readonly TimeSpan _ttl;
    private readonly int _capacity;

    public IssuedOrderWindow(Func<DateTimeOffset> nowUtc, TimeSpan ttl, int capacity)
    {
        ArgumentNullException.ThrowIfNull(nowUtc);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "IssuedOrderWindow: ttl must be positive.");
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "IssuedOrderWindow: capacity must be at least 1.");
        }

        _nowUtc = nowUtc;
        _ttl = ttl;
        _capacity = capacity;
    }

    public void Record(Guid orderId)
    {
        lock (_issuedAt)
        {
            _issuedAt[orderId] = _nowUtc();

            while (_issuedAt.Count > _capacity)
            {
                var oldestKey = _issuedAt.OrderBy(kvp => kvp.Value).First().Key;
                _issuedAt.Remove(oldestKey);
            }
        }
    }

    /// <summary>True while <paramref name="orderId"/> was <see cref="Record"/>-ed within the last <c>ttl</c>. An expired entry is evicted on the read that finds it, so the window never answers <see langword="true"/> for a stale entry it happens to still be holding.</summary>
    public bool IsRecentlyIssued(Guid orderId)
    {
        lock (_issuedAt)
        {
            if (!_issuedAt.TryGetValue(orderId, out var issuedAt))
            {
                return false;
            }

            if (_nowUtc() - issuedAt > _ttl)
            {
                _issuedAt.Remove(orderId);
                return false;
            }

            return true;
        }
    }

    public int Count
    {
        get
        {
            lock (_issuedAt)
            {
                return _issuedAt.Count;
            }
        }
    }
}
