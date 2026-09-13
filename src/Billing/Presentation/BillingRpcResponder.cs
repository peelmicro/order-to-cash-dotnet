using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Queries;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Infrastructure.Observability;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Presentation;

/// <summary>
/// ONE <see cref="BackgroundService"/>, FIVE subjects — <c>billing.credit.hold</c>,
/// <c>billing.credit.release</c>, <c>billing.credit.list</c>,
/// <c>billing.invoice.issue</c>, <c>billing.invoice.list</c> (design.md
/// §4.1, §4.2, `BI31`). One transport (NATS), five subscription loops
/// running CONCURRENTLY. Per message: extract <see cref="RpcMeta"/> where
/// required (`BC1`, `BI2`) -&gt; deserialise with <see cref="RpcJson"/> -&gt;
/// validate (§4.4) -&gt; resolve <see cref="IDispatcher"/> from a FRESH DI
/// scope -&gt; dispatch -&gt; reply. Never throws and never leaves a
/// request unanswered — every path is wrapped, and the catch replies a
/// mapped <see cref="RpcErrorPayload"/> (§4.5), the rule
/// <c>StockRpcResponder</c>/<c>OrdersCreateResponder</c> already follow.
/// </summary>
/// <remarks>
/// Renamed from <c>CreditRpcResponder</c> (`BI31`, design.md §4.1,
/// gate-approved 2026-09-06): #7 declared a SECOND controller for invoicing
/// and paid nothing, because NestJS supplied concurrency, per-request DI
/// scope and graceful shutdown. In #8 all three are hand-built inside this
/// ONE class and each is separately armed — `BC21` (a distinct scope per
/// request), `BC22` (the drain, both halves), backlog id 50 (individually
/// awaited fault isolation). A second responder class would fork four
/// armed behaviours into an UNGUARDED copy, and `CLAUDE.md`'s non-negotiable
/// is one <see cref="BackgroundService"/> per transport — Billing's RPC
/// transport is one. So this class is EXTENDED, not forked: everything
/// below this remark that predates this feature is otherwise UNCHANGED —
/// the <see cref="SemaphoreSlim"/> bound acquired before the scope, one
/// <see cref="IServiceScope"/> per request, the <c>_inFlight</c> set, the
/// individually-awaited <see cref="StopAsync"/> drain, the never-throws
/// try/catch.
/// </remarks>
public sealed class BillingRpcResponder(
    INatsConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<BillingResponderOptions> options,
    ILogger<BillingRpcResponder> logger) : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(options.Value.MaxConcurrentRequests, options.Value.MaxConcurrentRequests);
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new[]
        {
            SubscribeLoopAsync(CreditSubjects.CreditHold, stoppingToken),
            SubscribeLoopAsync(CreditSubjects.CreditRelease, stoppingToken),
            SubscribeLoopAsync(CreditSubjects.CreditList, stoppingToken),
            SubscribeLoopAsync(InvoiceSubjects.InvoiceIssue, stoppingToken),
            SubscribeLoopAsync(InvoiceSubjects.InvoiceList, stoppingToken),
            SubscribeLoopAsync(InvoiceSubjects.PaymentRegister, stoppingToken),
        };

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Drain whatever is still in flight rather than tearing down a
        // half-committed transaction (design.md §4.7). Every in-flight task
        // is awaited to completion — the drain is the point — but each
        // task's outcome is observed INDIVIDUALLY: Task.WhenAll rethrows
        // only the first fault, which would abort the host's shutdown
        // sequence over one request whose reply could not be delivered
        // (backlog id 50, `BC22`).
        var pending = _inFlight.Keys.ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var faults = await Task.WhenAll(pending.Select(WrapAsync)).ConfigureAwait(false);

        foreach (var fault in faults)
        {
            if (fault is not null)
            {
                logger.LogWarning(fault, "{Responder} shutdown: an in-flight request faulted while draining.", nameof(BillingRpcResponder));
            }
        }
    }

    private static async Task<Exception?> WrapAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
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
            // The bound is acquired BEFORE the scope, never inside it — `BC21`'s own ordering.
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
    /// separate from the NATS reply above so <c>CreditResponderConcurrencyTests</c>
    /// can prove "a distinct <see cref="IServiceScope"/> per request, never
    /// one per responder" (`BC21`) with a real <see cref="IServiceProvider"/>
    /// and a fake <see cref="IDispatcher"/> — no NATS connection, no host.
    /// </summary>
    internal async Task<byte[]> ProcessRequestAsync(string subject, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        // design.md §5.2, ledger L21/L24 — the ONE extraction site this
        // responder's six subjects share: CONTINUES the caller's trace (a
        // fresh span id, the SAME trace id) rather than starting an
        // unrelated one.
        var context = TraceContext.ExtractNats(message.Headers);
        using var activity = context is { } parent
            ? OtcActivity.Source.StartActivity($"rpc {subject}", ActivityKind.Server, parentContext: parent)
            : OtcActivity.Source.StartActivity($"rpc {subject}", ActivityKind.Server);

        // design.md §6's scope-push table, RPC responder row —
        // x-correlation-id from the request headers, when present.
        using var correlationScope = message.Headers is { } headers
            && headers.TryGetLastValue("x-correlation-id", out var rawCorrelationId)
            && Guid.TryParse(rawCorrelationId, out var correlationId)
                ? logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId })
                : null;

        // ONE IServiceScope PER REQUEST — never one per responder (`BC21`).
        using var scope = scopeFactory.CreateScope();

        try
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            return await DispatchAsync(subject, message, dispatcher, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Subject} failed: {Message}", subject, ex.Message);

            var errorPayload = BillingErrorMapper.Map(ex, DateTimeOffset.UtcNow);
            return RpcJson.Serialize(errorPayload);
        }
    }

    /// <summary>
    /// The per-subject dispatch logic, factored out from the NATS/DI
    /// plumbing above and made <see langword="internal"/> so
    /// <c>CreditResponderHeaderTests</c>/<c>InvoiceResponderValidationTests</c>'
    /// unit half can drive it against a fake <see cref="IDispatcher"/> with
    /// no real NATS connection and no host.
    /// </summary>
    internal static async Task<byte[]> DispatchAsync(string subject, NatsMsg<byte[]> message, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (message.Data is null)
        {
            throw new InvalidCreditRequestError($"{subject} request carried no payload.");
        }

        return subject switch
        {
            CreditSubjects.CreditHold => await HandleHoldAsync(dispatcher, message, cancellationToken).ConfigureAwait(false),
            CreditSubjects.CreditRelease => await HandleReleaseAsync(dispatcher, message, cancellationToken).ConfigureAwait(false),
            CreditSubjects.CreditList => await HandleCreditListAsync(dispatcher, message.Data, cancellationToken).ConfigureAwait(false),
            InvoiceSubjects.InvoiceIssue => await HandleInvoiceIssueAsync(dispatcher, message, cancellationToken).ConfigureAwait(false),
            InvoiceSubjects.InvoiceList => await HandleInvoiceListAsync(dispatcher, message.Data, cancellationToken).ConfigureAwait(false),
            InvoiceSubjects.PaymentRegister => await HandlePaymentRegisterAsync(dispatcher, message, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unrecognised subject '{subject}'."),
        };
    }

    private static async Task<byte[]> HandleHoldAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<CreditHoldRequestPayload>(message.Data);
        CreditRequestValidator.ValidateHold(request);

        // BC1: BOTH headers are required, and any failure is VALIDATION_FAILED
        // — mutating nothing, dispatching nothing.
        var meta = RequireMeta(message.Headers, CreditSubjects.CreditHold);

        var command = new HoldCreditCommand(request.OrderReference, request.RetailerCode, request.CompanyCode, request.Amount.Amount, request.Amount.Currency, meta.CorrelationId, meta.RequestId);
        var reply = await dispatcher.SendAsync<HoldCreditCommand, CreditHoldReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleReleaseAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<CreditReleaseRequestPayload>(message.Data);
        CreditRequestValidator.ValidateRelease(request);

        var meta = RequireMeta(message.Headers, CreditSubjects.CreditRelease);

        var command = new ReleaseCreditCommand(request.OrderReference, request.RetailerCode, request.CompanyCode, meta.CorrelationId, meta.RequestId);
        var reply = await dispatcher.SendAsync<ReleaseCreditCommand, CreditReleaseReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleCreditListAsync(IDispatcher dispatcher, byte[] data, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<CreditListRequestPayload>(data);
        CreditRequestValidator.ValidateList(request);

        var reply = await dispatcher.QueryAsync<ListCreditQuery, CreditListReplyPayload>(new ListCreditQuery(request), cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    /// <summary>
    /// `BI2`'s own wording: headers are extracted and validated BEFORE the
    /// payload is deserialised — the opposite order from the hold/release
    /// arms above, whose ordering predates this feature and is not this
    /// feature's to churn (design.md §4.1). Both throw before dispatch
    /// either way; this ordering is what `InvoiceResponderValidationTests`'
    /// entry-observation case exercises.
    /// </summary>
    private static async Task<byte[]> HandleInvoiceIssueAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var meta = RequireMeta(message.Headers, InvoiceSubjects.InvoiceIssue);

        if (message.Data is null)
        {
            throw new InvalidInvoiceRequestError($"{InvoiceSubjects.InvoiceIssue} request carried no payload.");
        }

        var request = RpcJson.Deserialize<InvoiceIssueRequestPayload>(message.Data);
        InvoiceRequestValidator.ValidateIssue(request);

        var command = new IssueInvoiceCommand(
            request.OrderReference,
            request.RetailerCode,
            request.CompanyCode,
            request.Currency,
            request.Lines,
            request.Discount,
            meta.CorrelationId,
            meta.RequestId);

        var reply = await dispatcher.SendAsync<IssueInvoiceCommand, InvoiceIssueReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static async Task<byte[]> HandleInvoiceListAsync(IDispatcher dispatcher, byte[] data, CancellationToken cancellationToken)
    {
        var request = RpcJson.Deserialize<InvoiceListRequestPayload>(data);
        InvoiceRequestValidator.ValidateList(request);

        var reply = await dispatcher.QueryAsync<ListInvoicesQuery, InvoiceListReplyPayload>(new ListInvoicesQuery(request), cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    /// <summary>Feature 22 — headers extracted and validated BEFORE the payload is deserialised, the SAME ordering <see cref="HandleInvoiceIssueAsync"/> already uses.</summary>
    private static async Task<byte[]> HandlePaymentRegisterAsync(IDispatcher dispatcher, NatsMsg<byte[]> message, CancellationToken cancellationToken)
    {
        var meta = RequireMeta(message.Headers, InvoiceSubjects.PaymentRegister);

        if (message.Data is null)
        {
            throw new InvalidInvoiceRequestError($"{InvoiceSubjects.PaymentRegister} request carried no payload.");
        }

        var request = RpcJson.Deserialize<PaymentRegisterRequestPayload>(message.Data);
        PaymentRegisterRequestValidator.ValidateRegister(request);

        var command = new RegisterPaymentCommand(
            request.InvoiceId,
            request.InvoiceReference,
            request.PaymentReference,
            request.Amount.Amount,
            request.Amount.Currency,
            request.ValueDate,
            request.Source,
            meta.CorrelationId,
            meta.RequestId);

        var reply = await dispatcher.SendAsync<RegisterPaymentCommand, PaymentRegisterReplyPayload>(command, cancellationToken).ConfigureAwait(false);

        return RpcJson.Serialize(reply);
    }

    private static RpcMeta RequireMeta(NatsHeaders? headers, string subject)
    {
        if (!RpcMetaExtractor.TryExtract(headers, out var meta, out var error))
        {
            throw new InvalidCreditRequestError($"{subject}: {error}");
        }

        return meta;
    }
}
