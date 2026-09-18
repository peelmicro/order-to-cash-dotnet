using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 110 (retroactive ported-idiom ledger, boundary RL5):
/// <see cref="NatsSagaCommandsAdapter"/>'s reply decode had no
/// <c>try</c>/<c>catch (JsonException)</c>, unlike its sibling
/// <see cref="NatsStockAvailabilityChecker"/> (feature 46 Advisory A1) and
/// unlike #7's <c>nats-saga-commands.adapter.ts:170-178</c> — a malformed
/// (non-JSON) reply body threw a bare <see cref="System.Text.Json.JsonException"/>
/// instead of the classified <see cref="SagaCommandTransportError"/> the saga
/// retry/terminal-classification logic expects. Mirrors
/// <c>NatsStockAvailabilityCheckerTests.AMalformedNonJsonReply_ThrowsStockCheckTransportError_NeverABareJsonException</c>,
/// over a REAL broker with a stand-in responder — never a mocked
/// <see cref="INatsConnection"/> (this feature's own testing discipline,
/// carried over from <c>NatsSagaCommandsAdapter</c>'s own class remark: the
/// real NATS request-reply surface is proven with a real broker, never
/// mocked).
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class NatsSagaCommandsAdapterReplyDecodeGuardTests(NatsContainerFixture nats)
{
    /// <summary>
    /// <c>stock.reserve</c> stands in for all six saga-command subjects here
    /// (the acceptance text names all six; the decode-guard code path is the
    /// SAME <c>SendAsync&lt;TRequest,TReply&gt;</c> for every one of them, so
    /// this one subject proves the shared seam without repeating five
    /// mechanically identical cases).
    /// </summary>
    [Fact]
    public async Task R110_AMalformedNonJsonReplyOnASagaCommandSubject_ThrowsSagaCommandTransportError_NeverABareJsonException()
    {
        await using var responder = await MalformedStandInResponder.StartAsync(nats.Url, RpcSubjects.StockReserve, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var adapter = new NatsSagaCommandsAdapter(connection, Options.Create(new OrdersSagaOptions()));

        var request = new StockReserveRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", [new StockReserveRequestLine("SKU-1", 1)]);
        var meta = new SagaCommandMeta(UniqueId.New(), UniqueId.New());

        var error = await Assert.ThrowsAsync<SagaCommandTransportError>(
            () => adapter.ReserveStockAsync(request, meta, CancellationToken.None));

        Assert.Equal(RpcSubjects.StockReserve, error.Subject);
    }
}

/// <summary>
/// A minimal real NATS responder answering EVERY request on a given subject
/// with bytes that are not valid JSON at all — the same shape as
/// <see cref="StandInFulfillmentStockCheckResponder.StartMalformedAsync"/>,
/// for the saga-command subjects. <c>StandInSagaResponders</c>' own
/// <c>StandInRpcResponder&lt;TRequest,TReply&gt;</c> always answers with a
/// typed, valid <c>TReply</c> serialised through <c>RpcJson.Serialize</c>, so
/// it cannot stand in for this malformed-body case; this type exists only to
/// answer raw, garbled bytes.
/// </summary>
internal sealed class MalformedStandInResponder : IAsyncDisposable
{
    private static readonly byte[] _malformedReply = "not-json-at-all"u8.ToArray();

    private readonly INatsConnection _connection;
    private readonly string _subject;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private MalformedStandInResponder(INatsConnection connection, string subject)
    {
        _connection = connection;
        _subject = subject;
        _loop = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Starts the stand-in and blocks until a real round-trip probe confirms
    /// the subscription is live — <c>StandInRpcResponder&lt;,&gt;</c>'s own
    /// discipline, reused via <see cref="SagaIntegrationTestSupport.WaitUntilReachableAsync"/>.
    /// This responder answers EVERY request (including the probe itself)
    /// with the same malformed bytes, so no dedicated probe-marker check is
    /// needed the way the typed stand-ins require.
    /// </summary>
    public static async Task<MalformedStandInResponder> StartAsync(string natsUrl, string subject, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var responder = new MalformedStandInResponder(connection, subject);

        try
        {
            await SagaIntegrationTestSupport.WaitUntilReachableAsync(connection, subject, _malformedReply, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await responder.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallowed deliberately (StandInFulfillmentStockCheckResponder's
                // own precedent) — the probe's own failure, preserved by the
                // bare `throw;` below, is what must reach the caller.
            }

            throw;
        }

        return responder;
    }

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
            await DisposeConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            // A PING/PONG round trip AFTER the UNSUB fences: once it
            // returns, the server is guaranteed to no longer route new
            // requests to this subscription (StandInFulfillmentStockCheckResponder's
            // own remarks on NatsSubEvents.OnSubscribed's documented gap).
            await _connection.PingAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort fence only — a connection already broken has
            // nothing left to fence.
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _connection.SubscribeAsync<byte[]>(_subject, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            await message.ReplyAsync(_malformedReply, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
