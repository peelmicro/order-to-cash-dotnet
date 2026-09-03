using NATS.Client.Core;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// A real NATS responder standing in for Fulfillment's own
/// <c>fulfillment.stock.check</c> responder (feature 17, not built yet) —
/// the shape orders_acceptance's own brief calls for: "a test NATS
/// responder standing in for Fulfillment is legitimate; a mocked NATS
/// client is not, because the transport is the thing under test". This
/// subscribes on a SEPARATE, real <see cref="INatsConnection"/> and answers
/// over the wire exactly like a real Fulfillment process would — the
/// <see cref="Orders.Infrastructure.Messaging.NatsStockAvailabilityChecker"/>
/// under test has no idea it is talking to a test double rather than
/// Fulfillment.
/// </summary>
internal sealed class StandInFulfillmentStockCheckResponder : IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    /// <param name="answer">Returning <see langword="null"/> means "received the request, deliberately send no reply" — <see cref="StartSilentAsync"/>'s shape for a Fulfillment that is up (subscribed) but never answers, review D1's TIMEOUT case.</param>
    private StandInFulfillmentStockCheckResponder(INatsConnection connection, Func<StockCheckRequestPayload, StockCheckReplyPayload?> answer)
    {
        _connection = connection;
        _loop = RunAsync(answer, _cts.Token);
    }

    /// <summary>
    /// Starts the stand-in and blocks until it has proven it is actually
    /// subscribed (a real round-trip probe, never a fixed delay). If the
    /// probe never succeeds, the partially-constructed instance — whose
    /// background subscribe loop is already running by the time the probe
    /// runs — is disposed before the exception propagates: an instance the
    /// caller's <c>await using</c> never received would otherwise leak a
    /// live subscription for the rest of the test run, which is exactly
    /// the kind of stray responder that would answer a LATER test's real
    /// request out of turn. A disposal fault on that cleanup path is
    /// deliberately swallowed (review D2): the probe's own exception is the
    /// one the caller needs, and a fault tearing down an already-broken
    /// connection is not new information.
    /// </summary>
    public static async Task<StandInFulfillmentStockCheckResponder> StartAsync(string natsUrl, Func<StockCheckRequestPayload, StockCheckReplyPayload?> answer, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var responder = new StandInFulfillmentStockCheckResponder(connection, answer);
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
                // Swallowed deliberately — see the remarks above. The probe's
                // own failure, preserved by the bare `throw;` below, is what
                // must reach the caller.
            }

            throw;
        }

        return responder;
    }

    /// <summary>Convenience factory: always answers with every requested line fully available.</summary>
    public static Task<StandInFulfillmentStockCheckResponder> StartAvailableAsync(string natsUrl, CancellationToken cancellationToken) =>
        StartAsync(
            natsUrl,
            request => new StockCheckReplyPayload(
                true,
                request.Lines.Select(line => new StockCheckReplyLine(line.ProductCode, line.Quantity, line.Quantity, true)).ToList()),
            cancellationToken);

    /// <summary>Convenience factory: always answers with every requested line short (zero on hand) — R31/R33's rejection path.</summary>
    public static Task<StandInFulfillmentStockCheckResponder> StartUnavailableAsync(string natsUrl, CancellationToken cancellationToken) =>
        StartAsync(
            natsUrl,
            request => new StockCheckReplyPayload(
                false,
                request.Lines.Select(line => new StockCheckReplyLine(line.ProductCode, line.Quantity, 0, false)).ToList()),
            cancellationToken);

    /// <summary>
    /// Review D1's TIMEOUT case: a Fulfillment that IS up — genuinely
    /// subscribed, so <c>NatsNoRespondersException</c> never fires — but
    /// never answers a real request, forcing the CALLER's own
    /// <c>NatsSubOpts.Timeout</c> to elapse. Distinguishes the harness's own
    /// startup probe (<c>companyCode == "PROBE"</c>, answered so
    /// <see cref="StartAsync"/> can still confirm subscription) from every
    /// other request (silently dropped) — the same wire, the same
    /// subscription, deliberately no reply to the request under test.
    /// </summary>
    public static Task<StandInFulfillmentStockCheckResponder> StartSilentAsync(string natsUrl, CancellationToken cancellationToken) =>
        StartAsync(
            natsUrl,
            request => request.CompanyCode == "PROBE"
                ? new StockCheckReplyPayload(true, [new StockCheckReplyLine("PROBE", 1, 1, true)])
                : null,
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected — exactly what cancelling the loop above causes.
        }
        finally
        {
            // Review D2: this block MUST run regardless of how the try above
            // exited — including a fault from _loop other than
            // OperationCanceledException (a NatsException on a dropped
            // connection, a malformed-message deserialise throw, ...) that
            // the catch above does not swallow and that therefore
            // propagates once this finally completes. Before this fix, such
            // a fault skipped the fence-and-dispose below entirely, leaking
            // exactly the live subscribed connection this type exists to
            // tear down — the same leftover-responder-answers-a-later-test
            // failure diagnosed once already, reached by a different door.
            await DisposeConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            // Cancelling the subscribe loop sends UNSUB but does not itself
            // wait for the server to have processed it — the exact
            // "publishers using OTHER connections may still race with the
            // server processing the subscription" gap NATS.Client.Core's
            // own docs name for the SUBSCRIBE side (NatsSubEvents.OnSubscribed's
            // remarks), mirrored here for teardown: a PING/PONG round-trip
            // on THIS connection, sent after the UNSUB, cannot complete
            // until the server has processed everything before it (protocol
            // messages over one connection are processed in order) — so
            // once it returns, the server is guaranteed to no longer route
            // new requests to this subscription. Without this fence, a
            // later test's real request could still race a fraction of a
            // second of in-flight teardown and get answered by THIS
            // stand-in instead of its own.
            await _connection.PingAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort fence only (review D2). A connection already
            // broken has nothing left to fence, and this must never be the
            // reason a genuine fault — the caller's own, or _loop's —
            // fails to reach whoever needs to see it.
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task WaitUntilSubscribedAsync(CancellationToken cancellationToken)
    {
        // A real round trip, not a fixed delay: repeatedly ping the subject
        // with a short per-attempt timeout until SOME reply arrives, which
        // is only possible once SubscribeAsync's server-side registration
        // has actually landed.
        var probe = new StockCheckRequestPayload("PROBE", [new StockCheckRequestLine("PROBE", 1)]);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await _connection.RequestAsync<byte[], byte[]>(
                    RpcSubjects.StockCheck,
                    RpcJson.Serialize(probe),
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
                // Nobody subscribed yet (or answered this particular probe
                // in time) — retry. Empirically, NATS.Client.Core 3.2.0
                // throws rather than returning a NatsMsg with a null Data.
            }
            catch (NatsNoRespondersException)
            {
                // The immediate 503 sentinel — this connection's own
                // SubscribeAsync has been called but the server has not yet
                // finished registering it (SubscribeAsync's own doc: the
                // subscription is not established until the enumerable is
                // iterated, and iteration races the server's processing of
                // the SUB message). Retry exactly like a reply timeout.
            }
        }

        throw new TimeoutException("Stand-in Fulfillment responder never became reachable.");
    }

    private async Task RunAsync(Func<StockCheckRequestPayload, StockCheckReplyPayload?> answer, CancellationToken cancellationToken)
    {
        await foreach (var message in _connection.SubscribeAsync<byte[]>(RpcSubjects.StockCheck, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (message.Data is null)
            {
                continue;
            }

            var request = RpcJson.Deserialize<StockCheckRequestPayload>(message.Data);
            var reply = answer(request);
            if (reply is null)
            {
                continue;
            }

            await message.ReplyAsync(RpcJson.Serialize(reply), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
