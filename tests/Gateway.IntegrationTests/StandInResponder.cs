using NATS.Client.Core;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// A real NATS responder standing in for an upstream service on ONE
/// subject — the shape <c>StandInFulfillmentStockCheckResponder</c>
/// (<c>tests/Orders.IntegrationTests</c>) establishes: a SEPARATE, real
/// <see cref="INatsConnection"/> answering over the wire exactly like a
/// real process would, so <see cref="OrderToCash.Gateway.Infrastructure.Messaging.NatsRpcClient"/>
/// under test has no idea it is talking to a test double. Readiness is
/// confirmed by a PACED retry loop (a real round trip per attempt, never a
/// fixed delay and never N attempts at the FULL per-call timeout — backlog
/// id 63's own warning: <c>NatsNoRespondersException</c> returns near-
/// instantly, so an unpaced loop can exhaust in ~1ms).
/// </summary>
public sealed class StandInResponder : IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly string _subject;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    /// <summary>The headers of the last NON-PROBE request this responder received — lets a test assert <c>x-correlation-id</c>/<c>x-request-id</c> actually arrived on the wire.</summary>
    public NatsHeaders? LastRequestHeaders { get; private set; }

    private StandInResponder(INatsConnection connection, string subject, Func<byte[], byte[]?> rawAnswer)
    {
        _connection = connection;
        _subject = subject;
        _loop = RunAsync(rawAnswer, _cts.Token);
    }

    public static Task<StandInResponder> StartAsync(string natsUrl, string subject, Func<byte[], byte[]?> rawAnswer, CancellationToken cancellationToken) =>
        StartCoreAsync(natsUrl, subject, rawAnswer, cancellationToken);

    /// <summary>Genuinely subscribed, but never replies to any request other than the readiness probe — forces the CALLER's own timeout to elapse (<c>NatsNoReplyException</c>).</summary>
    public static Task<StandInResponder> StartSilentAsync(string natsUrl, string subject, CancellationToken cancellationToken) =>
        StartCoreAsync(natsUrl, subject, _ => null, cancellationToken);

    private static async Task<StandInResponder> StartCoreAsync(string natsUrl, string subject, Func<byte[], byte[]?> rawAnswer, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });

        // Probe requests carry this exact one-byte payload; a real
        // responder under test never receives it because the readiness
        // probe subscribes on the SAME subject as production traffic, so
        // rawAnswer must special-case it — every StandInResponder factory
        // below wraps rawAnswer to do so via WrapWithProbeReply.
        var responder = new StandInResponder(connection, subject, WrapWithProbeReply(rawAnswer));

        try
        {
            await responder.WaitUntilSubscribedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await responder.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallowed deliberately — the probe's own failure is what must reach the caller.
            }

            throw;
        }

        return responder;
    }

    private static Func<byte[], byte[]?> WrapWithProbeReply(Func<byte[], byte[]?> rawAnswer) => data =>
        data.Length == 1 && data[0] == ProbeByte ? _probeReply : rawAnswer(data);

    private const byte ProbeByte = 0xFE;
    private static readonly byte[] _probeReply = [0xFF];

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            // A PING/PONG fence after the UNSUB — protocol messages over
            // one connection are processed in order, so once this
            // returns the server is guaranteed to no longer route new
            // requests to this subscription (mirrors
            // StandInFulfillmentStockCheckResponder's own remarks).
            await _connection.PingAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task WaitUntilSubscribedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await _connection.RequestAsync<byte[], byte[]>(
                    _subject,
                    [ProbeByte],
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
            }
            catch (NatsNoRespondersException)
            {
            }
        }

        throw new TimeoutException($"Stand-in responder for '{_subject}' never became reachable.");
    }

    private async Task RunAsync(Func<byte[], byte[]?> rawAnswer, CancellationToken cancellationToken)
    {
        await foreach (var message in _connection.SubscribeAsync<byte[]>(_subject, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (message.Data is null)
            {
                continue;
            }

            var isProbe = message.Data.Length == 1 && message.Data[0] == ProbeByte;
            if (!isProbe)
            {
                LastRequestHeaders = message.Headers;
            }

            var replyBytes = rawAnswer(message.Data);
            if (replyBytes is null)
            {
                continue;
            }

            await message.ReplyAsync(replyBytes, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
