using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using NATS.Client.Core;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// D9 (review round 3) — a thin <see cref="INatsConnection"/> DECORATOR
/// around a REAL connection, used only to make L21's two-concurrent-calls
/// guards create the overlap they need THEMSELVES, over the real
/// connection, rather than relying on an <c>await Task.Delay(...)</c>
/// between starting call A and call B — which a real connection's own
/// build-to-send latency closes long before the second call ever starts
/// building its headers (review round 3, D9's own finding).
///
/// <see cref="RequestAsync{TRequest,TReply}"/> is the EXACT
/// <see cref="INatsClient"/> member <c>NatsStockAvailabilityChecker</c>/
/// <c>NatsRpcClient</c> call — never an extension method — so intercepting
/// it here intercepts the real call site. Every other member is a plain
/// pass-through to <paramref name="inner"/>: this class changes nothing
/// about the two calls' construction, dispatch or headers, only WHEN each
/// call's own <c>RequestAsync</c> invocation is allowed to reach the real
/// connection.
/// </summary>
internal sealed class RequestOverlapBarrierConnection(INatsConnection inner) : INatsConnection
{
    private readonly Barrier _barrier = new(2);

    // ----- INatsClient -----
    public INatsConnection Connection => this;

    public ValueTask ConnectAsync() => inner.ConnectAsync();

    public ValueTask<TimeSpan> PingAsync(CancellationToken cancellationToken = default) => inner.PingAsync(cancellationToken);

    public ValueTask PublishAsync<T>(string subject, T data, NatsHeaders? headers = default, string? replyTo = default, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        inner.PublishAsync(subject, data, headers, replyTo, serializer, opts, cancellationToken);

    public ValueTask PublishAsync(string subject, NatsHeaders? headers = default, string? replyTo = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        inner.PublishAsync(subject, headers, replyTo, opts, cancellationToken);

    public ValueTask ReconnectAsync() => inner.ReconnectAsync();

    /// <summary>
    /// THE barrier. Blocks the calling thread until BOTH concurrent calls
    /// have reached this exact point — i.e. both callers have ALREADY
    /// built (and, under the L21 hoist mutation, written into the SAME
    /// shared) <see cref="NatsHeaders"/> instance, and NEITHER has yet had
    /// its request serialized/sent over the real wire. Whichever call's
    /// header values are in the shared object when the barrier releases
    /// both is what BOTH real sends observe under the mutation —
    /// reproducing the collision on EVERY run, never a probability, with
    /// NO delay added to the mutated production code itself (only this
    /// test-owned transport decorator waits).
    /// </summary>
    public async ValueTask<NatsMsg<TReply>> RequestAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default)
    {
        if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(10), cancellationToken))
        {
            throw new TimeoutException("RequestOverlapBarrierConnection: the second concurrent call never arrived at the barrier within 10s.");
        }

        return await inner.RequestAsync<TRequest, TReply>(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<NatsMsg<TReply>> RequestAsync<TReply>(string subject, INatsDeserialize<TReply>? replySerializer = default, NatsSubOpts? replyOpts = default, CancellationToken cancellationToken = default) =>
        inner.RequestAsync<TReply>(subject, replySerializer, replyOpts, cancellationToken);

    public IAsyncEnumerable<NatsMsg<T>> SubscribeAsync<T>(string subject, string? queueGroup = default, INatsDeserialize<T>? serializer = default, NatsSubOpts? opts = default, CancellationToken cancellationToken = default) =>
        inner.SubscribeAsync(subject, queueGroup, serializer, opts, cancellationToken);

    // ----- IAsyncDisposable -----
    public ValueTask DisposeAsync()
    {
        _barrier.Dispose();
        return inner.DisposeAsync();
    }

    // ----- INatsConnection -----
    public event AsyncEventHandler<NatsEventArgs>? ConnectionDisconnected
    {
        add => inner.ConnectionDisconnected += value;
        remove => inner.ConnectionDisconnected -= value;
    }

    public event AsyncEventHandler<NatsEventArgs>? ConnectionOpened
    {
        add => inner.ConnectionOpened += value;
        remove => inner.ConnectionOpened -= value;
    }

    public event AsyncEventHandler<NatsEventArgs>? ReconnectFailed
    {
        add => inner.ReconnectFailed += value;
        remove => inner.ReconnectFailed -= value;
    }

    public event AsyncEventHandler<NatsMessageDroppedEventArgs>? MessageDropped
    {
        add => inner.MessageDropped += value;
        remove => inner.MessageDropped -= value;
    }

    public event AsyncEventHandler<NatsSlowConsumerEventArgs>? SlowConsumerDetected
    {
        add => inner.SlowConsumerDetected += value;
        remove => inner.SlowConsumerDetected -= value;
    }

    public event AsyncEventHandler<NatsLameDuckModeActivatedEventArgs>? LameDuckModeActivated
    {
        add => inner.LameDuckModeActivated += value;
        remove => inner.LameDuckModeActivated -= value;
    }

    public event AsyncEventHandler<NatsServerErrorEventArgs>? ServerError
    {
        add => inner.ServerError += value;
        remove => inner.ServerError -= value;
    }

    public INatsServerInfo? ServerInfo => inner.ServerInfo;

    public NatsOpts Opts => inner.Opts;

    public NatsConnectionState ConnectionState => inner.ConnectionState;

    public INatsSubscriptionManager SubscriptionManager => inner.SubscriptionManager;

    public NatsHeaderParser HeaderParser => inner.HeaderParser;

    public Func<(string, int), ValueTask<(string, int)>>? OnConnectingAsync
    {
        get => inner.OnConnectingAsync;
        set => inner.OnConnectingAsync = value;
    }

    public Func<INatsSocketConnection, ValueTask<INatsSocketConnection>>? OnSocketAvailableAsync
    {
        get => inner.OnSocketAvailableAsync;
        set => inner.OnSocketAvailableAsync = value;
    }

    public ValueTask AddSubAsync(NatsSubBase sub, CancellationToken cancellationToken = default) =>
        inner.AddSubAsync(sub, cancellationToken);

    public ValueTask<NatsSub<TReply>> CreateRequestSubAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        inner.CreateRequestSubAsync(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken);

    public BoundedChannelOptions GetBoundedChannelOpts(NatsSubChannelOpts? subChannelOpts) =>
        inner.GetBoundedChannelOpts(subChannelOpts);

    public string NewInbox() => inner.NewInbox();

    public void OnMessageDropped<T>(NatsSubBase natsSub, int pending, NatsMsg<T> msg) =>
        inner.OnMessageDropped(natsSub, pending, msg);

    public ValueTask PublishAsync<T>(in NatsMsg<T> msg, INatsSerialize<T>? serializer = default, NatsPubOpts? opts = default, CancellationToken cancellationToken = default) =>
        inner.PublishAsync(in msg, serializer, opts, cancellationToken);

    public IAsyncEnumerable<NatsMsg<TReply>> RequestManyAsync<TRequest, TReply>(
        string subject,
        TRequest? data,
        NatsHeaders? headers = default,
        INatsSerialize<TRequest>? requestSerializer = default,
        INatsDeserialize<TReply>? replySerializer = default,
        NatsPubOpts? requestOpts = default,
        NatsSubOpts? replyOpts = default,
        CancellationToken cancellationToken = default) =>
        inner.RequestManyAsync(subject, data, headers, requestSerializer, replySerializer, requestOpts, replyOpts, cancellationToken);

    public ValueTask<INatsSub<T>> SubscribeCoreAsync<T>(
        string subject,
        string? queueGroup = default,
        INatsDeserialize<T>? serializer = default,
        NatsSubOpts? opts = default,
        CancellationToken cancellationToken = default) =>
        inner.SubscribeCoreAsync(subject, queueGroup, serializer, opts, cancellationToken);
}
