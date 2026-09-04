using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>
/// The five outbound saga commands (design.md §6.1) over the EXISTING
/// singleton <see cref="INatsConnection"/> — no second NATS connection is
/// created. One method per subject, reusing
/// <see cref="NatsStockAvailabilityChecker"/>'s shape verbatim in structure:
/// the shared connection, <see cref="RpcJson"/>, a per-call
/// <see cref="NatsSubOpts"/> timeout. The class name is fixed by feature 42's
/// own acceptance text — do not rename it.
/// </summary>
public sealed class NatsSagaCommandsAdapter : ISagaCommands
{
    /// <summary>
    /// The one NATS call this adapter needs — matching
    /// <c>INatsConnection.RequestAsync&lt;TRequest,TReply&gt;</c>'s own shape
    /// closely enough that the production delegate is a one-line wrapper.
    /// Public test seam (<c>KafkaFactPublisher</c>'s own precedent, "a
    /// caller may hand in an already-built producer"): unit tests substitute
    /// a fake delegate rather than a hand-rolled 30-member
    /// <see cref="INatsConnection"/> fake, because the real NATS
    /// request-reply surface the taxonomy below depends on
    /// (<see cref="NatsNoRespondersException"/>, <see cref="NatsNoReplyException"/>)
    /// is proven with a REAL broker in the integration suite, never mocked
    /// (this feature's own testing discipline).
    /// </summary>
    public delegate ValueTask<NatsMsg<byte[]>> RawRequester(string subject, byte[] payload, NatsSubOpts replyOpts, CancellationToken cancellationToken);

    private readonly RawRequester _request;
    private readonly IOptions<OrdersSagaOptions> _options;

    public NatsSagaCommandsAdapter(INatsConnection connection, IOptions<OrdersSagaOptions> options)
        : this(BuildRequester(connection), options)
    {
    }

    /// <summary>Test seam — see <see cref="RawRequester"/>.</summary>
    public NatsSagaCommandsAdapter(RawRequester request, IOptions<OrdersSagaOptions> options)
    {
        _request = request;
        _options = options;
    }

    public Task<StockReserveReplyPayload> ReserveStockAsync(StockReserveRequestPayload request, CancellationToken cancellationToken) =>
        SendAsync<StockReserveRequestPayload, StockReserveReplyPayload>(RpcSubjects.StockReserve, request, cancellationToken);

    public Task<StockReleaseReplyPayload> ReleaseStockAsync(StockReleaseRequestPayload request, CancellationToken cancellationToken) =>
        SendAsync<StockReleaseRequestPayload, StockReleaseReplyPayload>(RpcSubjects.StockRelease, request, cancellationToken);

    public Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, CancellationToken cancellationToken) =>
        SendAsync<DespatchCreateRequestPayload, DespatchCreateReplyPayload>(RpcSubjects.DespatchCreate, request, cancellationToken);

    public Task<CreditHoldReplyPayload> HoldCreditAsync(CreditHoldRequestPayload request, CancellationToken cancellationToken) =>
        SendAsync<CreditHoldRequestPayload, CreditHoldReplyPayload>(RpcSubjects.CreditHold, request, cancellationToken);

    public Task<InvoiceIssueReplyPayload> IssueInvoiceAsync(InvoiceIssueRequestPayload request, CancellationToken cancellationToken) =>
        SendAsync<InvoiceIssueRequestPayload, InvoiceIssueReplyPayload>(RpcSubjects.InvoiceIssue, request, cancellationToken);

    /// <summary>
    /// The taxonomy of design.md §6.1's table, in the ONE place it is
    /// classified — commented, per tasks.md D1/A5, as feature 42's seam:
    /// feature 42 splits the <c>RpcError</c>-body row on its <c>code</c>
    /// into a terminal-business set and a transient set. This feature keeps
    /// every <c>RpcError</c> body classified <see cref="SagaCommandTransportError"/>
    /// (retryable), deliberately, and does not implement that split.
    /// </summary>
    private async Task<TReply> SendAsync<TRequest, TReply>(string subject, TRequest request, CancellationToken cancellationToken)
    {
        var timeoutMs = _options.Value.Command.TimeoutMs;
        var replyOpts = new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

        NatsMsg<byte[]> reply;
        try
        {
            reply = await _request(subject, RpcJson.Serialize(request), replyOpts, cancellationToken).ConfigureAwait(false);
        }
        catch (NatsNoRespondersException)
        {
            // The IMMEDIATE 503 sentinel — no responder is subscribed at
            // all (design.md §8: the expected steady state until phases 9/10).
            throw new SagaCommandTransportError(subject, $"no responder is subscribed to {subject}.");
        }
        catch (NatsNoReplyException)
        {
            // A responder IS subscribed but the subscription's own Timeout
            // elapsed with no reply — empirically confirmed by feature 15:
            // NATS.Client.Core 3.2.0 throws this rather than returning a
            // NatsMsg whose Data is null.
            throw new SagaCommandTimeoutError(subject, timeoutMs);
        }

        if (reply.Data is null)
        {
            throw new SagaCommandTimeoutError(subject, timeoutMs);
        }

        if (IsRpcErrorBody(reply.Data))
        {
            var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data);
            // feature 42 splits this on `error.Code` — kept in ONE place,
            // deliberately not implemented here (design.md §6.1, §12).
            throw new SagaCommandTransportError(subject, $"{error.Code}: {error.Message}");
        }

        return RpcJson.Deserialize<TReply>(reply.Data);
    }

    /// <summary>
    /// The <c>RpcError</c> schema's two REQUIRED fields (<c>code</c>,
    /// <c>message</c>) appear together on no success reply payload this
    /// adapter deserialises (design.md §6.1's ten reply shapes never pair
    /// them) — a cheap, generic discriminator over the raw JSON that needs
    /// no second deserialisation attempt-and-catch.
    /// </summary>
    private static bool IsRpcErrorBody(ReadOnlyMemory<byte> data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("code", out _)
            && root.TryGetProperty("message", out _);
    }

    private static RawRequester BuildRequester(INatsConnection connection) =>
        (subject, payload, replyOpts, cancellationToken) =>
            connection.RequestAsync<byte[], byte[]>(subject, payload, replyOpts: replyOpts, cancellationToken: cancellationToken);
}
