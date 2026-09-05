using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Application.Commands;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Application.Queries;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;

namespace OrderToCash.Fulfillment.Presentation;

/// <summary>
/// ONE <see cref="BackgroundService"/>, six subjects — the five
/// <c>fulfillment.stock.*</c> subjects (design.md §6.1) plus
/// <c>fulfillment.despatch.create</c> (feature 18, joining the SAME NATS
/// transport rather than a second responder class — "one BackgroundService
/// per transport", CLAUDE.md). One transport (NATS), six subscription loops
/// running CONCURRENTLY. Per message: extract <see cref="RpcMeta"/> where
/// required (`FS3`) -&gt; deserialise with <see cref="RpcJson"/> -&gt;
/// validate (§6.4) -&gt; resolve <see cref="IDispatcher"/> from a FRESH DI
/// scope -&gt; dispatch -&gt; reply. Never throws and never leaves a request
/// unanswered — every path is wrapped, and the catch replies a mapped
/// <see cref="RpcErrorPayload"/> (§6.5), the rule <c>OrdersCreateResponder</c>
/// already follows.
/// </summary>
/// <remarks>
/// `FS18`/§6.2 — deliberately NOT <c>OrdersCreateResponder</c>'s sequential
/// shape. A <see cref="SemaphoreSlim"/> bound is acquired BEFORE the scope is
/// created (never inside it) and released once the request's task completes;
/// every request gets its OWN <see cref="IServiceScope"/>, never one per
/// responder; in-flight tasks are tracked so <see cref="StopAsync"/> can
/// await them within the host's shutdown timeout rather than tearing down a
/// half-committed transaction.
/// </remarks>
public sealed class StockRpcResponder(
    INatsConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<StockResponderOptions> options,
    ILogger<StockRpcResponder> logger) : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(options.Value.MaxConcurrentRequests, options.Value.MaxConcurrentRequests);
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new[]
        {
            SubscribeLoopAsync(StockSubjects.StockCheck, stoppingToken),
            SubscribeLoopAsync(StockSubjects.StockReserve, stoppingToken),
            SubscribeLoopAsync(StockSubjects.StockRelease, stoppingToken),
            SubscribeLoopAsync(StockSubjects.StockList, stoppingToken),
            SubscribeLoopAsync(StockSubjects.StockReplenish, stoppingToken),
            SubscribeLoopAsync(StockSubjects.DespatchCreate, stoppingToken),
        };

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Drain whatever is still in flight rather than tearing down a
        // half-committed transaction (design.md §6.2).
        var pending = _inFlight.Keys.ToArray();
        if (pending.Length > 0)
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        _semaphore.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SubscribeLoopAsync(string subject, CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            // The bound is acquired BEFORE the scope, never inside it —
            // §6.2's own ordering.
            await _semaphore.WaitAsync(stoppingToken).ConfigureAwait(false);

            var task = HandleAsync(subject, message, stoppingToken);
            _inFlight.TryAdd(task, 0);

            _ = task.ContinueWith(
                completed =>
                {
                    _inFlight.TryRemove(completed, out _);
                    _semaphore.Release();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleAsync(string subject, NatsMsg<byte[]> message, CancellationToken stoppingToken)
    {
        var replyBytes = await ProcessRequestAsync(subject, message, stoppingToken).ConfigureAwait(false);
        await message.ReplyAsync(replyBytes, cancellationToken: stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The DI-and-dispatch half of handling one request, deliberately kept
    /// separate from the NATS reply above so <c>StockResponderConcurrencyTests</c>
    /// can prove "a distinct <see cref="IServiceScope"/> per request, never
    /// one per responder" (`FS18`) with a real
    /// <see cref="IServiceProvider"/> and a fake <see cref="IDispatcher"/> —
    /// no NATS connection, no host. Never throws: every path returns reply
    /// bytes, mapped through <see cref="StockErrorMapper"/> on failure — the
    /// rule <c>OrdersCreateResponder</c> already follows.
    /// </summary>
    internal async Task<byte[]> ProcessRequestAsync(string subject, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        // ONE IServiceScope PER REQUEST — never one per responder (`FS18`).
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        try
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            return await DispatchAsync(subject, message, dispatcher, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Subject} failed: {Message}", subject, ex.Message);

            var errorPayload = StockErrorMapper.Map(ex, clock.UtcNow);
            return RpcJson.Serialize(errorPayload);
        }
    }

    /// <summary>
    /// The per-subject dispatch logic, deliberately factored out from the
    /// NATS/DI plumbing above and made <see langword="internal"/>
    /// (<c>InternalsVisibleTo.cs</c>) so <c>StockResponderHeaderTests</c> and
    /// <c>StockWireTests</c>' unit half can drive it against a fake
    /// <see cref="IDispatcher"/> with no real NATS connection and no host.
    /// </summary>
    internal static async Task<byte[]> DispatchAsync(string subject, NatsMsg<byte[]> message, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (message.Data is null)
        {
            throw new InvalidStockRequestError($"{subject} request carried no payload.");
        }

        switch (subject)
        {
            case StockSubjects.StockCheck:
                return await HandleCheckAsync(dispatcher, message.Data, cancellationToken).ConfigureAwait(false);

            case StockSubjects.StockReserve:
                return await HandleReserveAsync(dispatcher, message, cancellationToken).ConfigureAwait(false);

            case StockSubjects.StockRelease:
                return await HandleReleaseAsync(dispatcher, message, cancellationToken).ConfigureAwait(false);

            case StockSubjects.StockList:
                return await HandleListAsync(dispatcher, message.Data, cancellationToken).ConfigureAwait(false);

            case StockSubjects.StockReplenish:
                return await HandleReplenishAsync(dispatcher, message.Data, cancellationToken).ConfigureAwait(false);

            case StockSubjects.DespatchCreate:
                return await HandleDespatchCreateAsync(dispatcher, message, cancellationToken).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unrecognised subject '{subject}'.");
        }
    }

    private static async Task<byte[]> HandleCheckAsync(IDispatcher dispatcher, byte[] data, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<StockCheckRequestPayload>(data);
        StockRequestValidator.ValidateCheck(request);

        var reply = await dispatcher.QueryAsync<CheckStockQuery, StockCheckReplyPayload>(
            new CheckStockQuery(request.CompanyCode, request.Lines), cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleReserveAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<StockReserveRequestPayload>(message.Data);
        StockRequestValidator.ValidateReserve(request);

        // FS3: BOTH headers are required, and any failure is VALIDATION_FAILED
        // — mutating nothing, dispatching nothing.
        var meta = RequireMeta(message.Headers, StockSubjects.StockReserve);

        var command = new ReserveStockCommand(request.OrderReference, request.RetailerCode, request.CompanyCode, request.Lines, meta.CorrelationId, meta.RequestId);
        var reply = await dispatcher.SendAsync<ReserveStockCommand, StockReserveReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleReleaseAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<StockReleaseRequestPayload>(message.Data);
        StockRequestValidator.ValidateRelease(request);

        var meta = RequireMeta(message.Headers, StockSubjects.StockRelease);

        var command = new ReleaseStockCommand(request.OrderReference, request.Reason, meta.CorrelationId, meta.RequestId);
        var reply = await dispatcher.SendAsync<ReleaseStockCommand, StockReleaseReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleListAsync(IDispatcher dispatcher, byte[] data, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<StockListRequestPayload>(data);
        StockRequestValidator.ValidateList(request);

        var reply = await dispatcher.QueryAsync<ListStockQuery, StockListReplyPayload>(new ListStockQuery(request), cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleReplenishAsync(IDispatcher dispatcher, byte[] data, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<StockReplenishRequestPayload>(data);
        StockRequestValidator.ValidateReplenish(request);

        var command = new ReplenishStockCommand(request.CompanyCode, request.Lines);
        var reply = await dispatcher.SendAsync<ReplenishStockCommand, StockReplenishReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleDespatchCreateAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<DespatchCreateRequestPayload>(message.Data);
        StockRequestValidator.ValidateDespatchCreate(request);

        // Mirrors FS3 for stock.reserve/.release: despatch.create is a saga
        // command too (Orders sends it via the SAME SagaCommandMeta-carrying
        // path), so a missing/malformed header is refused BEFORE dispatch —
        // a fact emitted without the order id would land on an arbitrary
        // Kafka partition and break per-order ordering for the orchestrator.
        var meta = RequireMeta(message.Headers, StockSubjects.DespatchCreate);

        var command = new CreateDespatchCommand(request.OrderReference, meta.CorrelationId, meta.RequestId);
        var reply = await dispatcher.SendAsync<CreateDespatchCommand, DespatchCreateReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static RpcMeta RequireMeta(NatsHeaders? headers, string subject)
    {
        if (!RpcMetaExtractor.TryExtract(headers, out var meta, out var error))
        {
            throw new InvalidStockRequestError($"{subject}: {error}");
        }

        return meta;
    }
}
