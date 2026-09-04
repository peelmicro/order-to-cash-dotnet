using System.Collections.Concurrent;
using System.Text.Json;
using Confluent.Kafka;
using NATS.Client.Core;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Outbox;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Five programmable NATS responders standing in for Fulfillment's and
/// Billing's own saga responders (features 17-22, not built yet) — the same
/// discipline <see cref="StandInFulfillmentStockCheckResponder"/> already
/// established: a real <see cref="INatsConnection"/> subscription, answered
/// over the wire exactly like a real responder would, with request recording
/// and a genuine subscribe-probe (never a fixed delay) before
/// <see cref="StandInRpcResponder{TRequest,TReply}.StartAsync"/> returns.
/// </summary>
/// <remarks>
/// Crucially the stand-ins must also stand in for the responders'
/// OUTBOX side: in the real system <c>stock.reserved.v1</c> and its
/// siblings arrive because Fulfillment or Billing committed and relayed
/// them. <see cref="PublishFactAsync{TPayload}"/> publishes the
/// corresponding fact envelope directly to the real Kafka topic, keyed by
/// <c>correlationId</c>, standing in for that outbox relay.
/// </remarks>
internal sealed class StandInRpcResponder<TRequest, TReply> : IAsyncDisposable
{
    /// <summary>A payload no real request ever produces — the subscribe-probe's own marker, so the probe never needs a type-specific "PROBE" sentinel field the way the single-subject precedent used <c>companyCode == "PROBE"</c>.</summary>
    private static readonly byte[] _probeMarker = "\"__otc_saga_probe__\""u8.ToArray();

    private readonly INatsConnection _connection;
    private readonly string _subject;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly ConcurrentQueue<TRequest> _requests = new();

    private StandInRpcResponder(INatsConnection connection, string subject, Func<TRequest, TReply?> answer)
    {
        _connection = connection;
        _subject = subject;
        _loop = RunAsync(answer, _cts.Token);
    }

    public IReadOnlyList<TRequest> ObservedRequests => [.. _requests];

    /// <summary>Starts the stand-in and blocks until a real round-trip probe confirms the subscription is live — review D2's discipline, reused.</summary>
    public static async Task<StandInRpcResponder<TRequest, TReply>> StartAsync(string natsUrl, string subject, Func<TRequest, TReply?> answer, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var responder = new StandInRpcResponder<TRequest, TReply>(connection, subject, answer);

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
                // Swallowed deliberately (review D2) — the probe's own
                // failure is what must reach the caller.
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

    private async Task WaitUntilSubscribedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await _connection.RequestAsync<byte[], byte[]>(
                    _subject,
                    _probeMarker,
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
                // Nobody subscribed yet — retry.
            }
            catch (NatsNoRespondersException)
            {
                // The immediate 503 sentinel — retry exactly like a timeout.
            }
        }

        throw new TimeoutException($"Stand-in responder for '{_subject}' never became reachable.");
    }

    private async Task RunAsync(Func<TRequest, TReply?> answer, CancellationToken cancellationToken)
    {
        await foreach (var message in _connection.SubscribeAsync<byte[]>(_subject, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (message.Data is null)
            {
                continue;
            }

            if (message.Data.AsSpan().SequenceEqual(_probeMarker))
            {
                await message.ReplyAsync(_probeMarker, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            TRequest request;
            try
            {
                request = RpcJson.Deserialize<TRequest>(message.Data);
            }
            catch (JsonException)
            {
                continue;
            }

            _requests.Enqueue(request);

            var reply = answer(request);
            if (reply is null)
            {
                // Deliberately no reply — the caller's own timeout fires.
                continue;
            }

            await message.ReplyAsync(RpcJson.Serialize(reply), cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>The five typed stand-in factories, plus the fact-publishing helper standing in for the responders' own outbox relay.</summary>
internal static class StandInSagaResponders
{
    public static Task<StandInRpcResponder<StockReserveRequestPayload, StockReserveReplyPayload>> StartStockReserveAsync(
        string natsUrl, Func<StockReserveRequestPayload, StockReserveReplyPayload?> answer, CancellationToken cancellationToken) =>
        StandInRpcResponder<StockReserveRequestPayload, StockReserveReplyPayload>.StartAsync(natsUrl, RpcSubjects.StockReserve, answer, cancellationToken);

    public static Task<StandInRpcResponder<StockReleaseRequestPayload, StockReleaseReplyPayload>> StartStockReleaseAsync(
        string natsUrl, Func<StockReleaseRequestPayload, StockReleaseReplyPayload?> answer, CancellationToken cancellationToken) =>
        StandInRpcResponder<StockReleaseRequestPayload, StockReleaseReplyPayload>.StartAsync(natsUrl, RpcSubjects.StockRelease, answer, cancellationToken);

    public static Task<StandInRpcResponder<DespatchCreateRequestPayload, DespatchCreateReplyPayload>> StartDespatchCreateAsync(
        string natsUrl, Func<DespatchCreateRequestPayload, DespatchCreateReplyPayload?> answer, CancellationToken cancellationToken) =>
        StandInRpcResponder<DespatchCreateRequestPayload, DespatchCreateReplyPayload>.StartAsync(natsUrl, RpcSubjects.DespatchCreate, answer, cancellationToken);

    public static Task<StandInRpcResponder<CreditHoldRequestPayload, CreditHoldReplyPayload>> StartCreditHoldAsync(
        string natsUrl, Func<CreditHoldRequestPayload, CreditHoldReplyPayload?> answer, CancellationToken cancellationToken) =>
        StandInRpcResponder<CreditHoldRequestPayload, CreditHoldReplyPayload>.StartAsync(natsUrl, RpcSubjects.CreditHold, answer, cancellationToken);

    public static Task<StandInRpcResponder<InvoiceIssueRequestPayload, InvoiceIssueReplyPayload>> StartInvoiceIssueAsync(
        string natsUrl, Func<InvoiceIssueRequestPayload, InvoiceIssueReplyPayload?> answer, CancellationToken cancellationToken) =>
        StandInRpcResponder<InvoiceIssueRequestPayload, InvoiceIssueReplyPayload>.StartAsync(natsUrl, RpcSubjects.InvoiceIssue, answer, cancellationToken);

    /// <summary>
    /// Publishes one fact envelope directly to a real Kafka topic, keyed by
    /// <c>correlationId</c> — standing in for the responder's own outbox
    /// relay (design.md §8.1). A short-lived idempotent producer, built and
    /// disposed per call; test harness code, not the production
    /// <see cref="KafkaFactPublisher"/>.
    /// </summary>
    public static async Task PublishFactAsync<TPayload>(
        string bootstrapServers,
        string topic,
        string eventType,
        Guid correlationId,
        Guid causationId,
        DateTimeOffset occurredAt,
        TPayload payload,
        CancellationToken cancellationToken,
        Guid? eventId = null)
    {
        using var producer = new ProducerBuilder<string, byte[]>(KafkaFactPublisher.BuildProducerConfig(new KafkaOptions { BootstrapServers = bootstrapServers })).Build();

        var envelope = new Envelope<TPayload>(eventId ?? Guid.NewGuid(), eventType, correlationId, correlationId, causationId, occurredAt, payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);

        await producer.ProduceAsync(
            topic,
            new Message<string, byte[]> { Key = correlationId.ToString(), Value = bytes },
            cancellationToken).ConfigureAwait(false);

        producer.Flush(cancellationToken);
    }
}
