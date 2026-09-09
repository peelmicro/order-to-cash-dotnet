using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Commands;

/// <summary>The scan reached the end of <c>billing.invoice.list</c> (a page shorter than the page size) without a match — there is nothing left to find. The only branch that may honestly claim the invoice does not exist.</summary>
public sealed class InvoiceNotFoundError(Guid invoiceId) : Exception($"no invoice for id \"{invoiceId}\"")
{
    public Guid InvoiceId { get; } = invoiceId;
}

/// <summary>The scan hit its page budget while the last page fetched was still full — <c>billing.invoice.list</c> may hold more invoices beyond the search window. This does NOT claim the invoice does not exist, so it is never a 404.</summary>
public sealed class InvoiceScanBudgetExceededError(Guid invoiceId, int scannedCount)
    : Exception($"invoice \"{invoiceId}\" was not found within the {scannedCount} most recently issued invoices scanned — it may exist beyond this gateway's search window")
{
    public Guid InvoiceId { get; } = invoiceId;

    public int ScannedCount { get; } = scannedCount;
}

/// <summary>The invoice was found, but its order has not been projected into the read model yet — the RPC call needs the order id as its correlationId, and none is available.</summary>
public sealed class OrderNotYetProjectedError(string orderReference) : Exception($"order \"{orderReference}\" is not yet projected into the read model — retry")
{
    public string OrderReference { get; } = orderReference;
}

public sealed record RegisterPaymentCommand(
    Guid InvoiceId,
    string PaymentReference,
    long Amount,
    string Currency,
    DateTimeOffset ValueDate,
    string Source) : ICommand<RegisterPaymentResult>;

public sealed record RegisterPaymentResult(PaymentRegisterReplyPayload Reply, Guid CorrelationId);

/// <summary>
/// <c>POST /invoices/{id}/payments</c> → NATS RPC
/// <c>billing.payment.register</c> (R47-R49). The path carries only
/// <c>invoiceId</c> (a Billing-internal id), but <c>saga.md</c>'s invariant
/// is <c>correlationId = orderId</c>, and Billing's RPC responder REJECTS a
/// request with no <c>x-correlation-id</c> header at all
/// (<c>BillingRpcResponder.RequireMeta</c> on <c>InvoiceSubjects.PaymentRegister</c>)
/// — so this handler must resolve the order id BEFORE calling
/// <c>billing.payment.register</c>, not after.
/// </summary>
/// <remarks>
/// Ported from #7's <c>apps/gateway/src/application/commands/register-payment.command.ts</c>,
/// including the bounded-scan gap it names and closes: there is no
/// "get invoice by id" RPC query in asyncapi.yaml — only
/// <c>billing.invoice.list</c>, filterable by
/// <c>status</c>/<c>retailerCode</c>/<c>companyCode</c>/<c>orderReference</c>/
/// <c>issuedBeforeMinutes</c>, never by <c>invoiceId</c> — and the read
/// model never learns <c>invoiceId</c> at all. Step 1 below is therefore a
/// BOUNDED SCAN of <c>billing.invoice.list</c> — an RPC call to Billing's
/// own read query, not a write-database read, but still O(invoices) rather
/// than O(1). Once the matching <c>InvoiceViewPayload.OrderReference</c> is
/// found, step 2 resolves the REAL order id from the read model's own
/// <c>_id</c> (R54's read-model-only rule).
/// </remarks>
public sealed class RegisterPaymentCommandHandler(IRpcClient rpc, IOrderReadModel readModel) : ICommandHandler<RegisterPaymentCommand, RegisterPaymentResult>
{
    private const int ScanPageSize = 200;
    private const int ScanMaxPages = 5;

    public async Task<RegisterPaymentResult> HandleAsync(RegisterPaymentCommand command, CancellationToken cancellationToken)
    {
        var orderReference = await ResolveOrderReferenceAsync(command.InvoiceId, cancellationToken).ConfigureAwait(false);

        var order = await readModel.FindByOrderReferenceAsync(orderReference, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            throw new OrderNotYetProjectedError(orderReference);
        }

        var payload = new PaymentRegisterRequestPayload(
            command.PaymentReference,
            new GatewayRpcMoney(command.Amount, command.Currency),
            command.ValueDate,
            command.Source,
            command.InvoiceId,
            InvoiceReference: null);

        var reply = await rpc.CallAsync<PaymentRegisterRequestPayload, PaymentRegisterReplyPayload>(
            GatewaySubjects.PaymentRegister,
            payload,
            new RpcCallMeta(order.OrderId, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);

        return new RegisterPaymentResult(reply, order.OrderId);
    }

    private async Task<string> ResolveOrderReferenceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var scannedCount = 0;

        for (var page = 1; page <= ScanMaxPages; page++)
        {
            var requestId = Guid.NewGuid();
            var listPayload = new InvoiceListRequestPayload(page, ScanPageSize, null, null, null, null, null);
            var listReply = await rpc.CallAsync<InvoiceListRequestPayload, InvoiceListReplyPayload>(
                GatewaySubjects.InvoiceList, listPayload, new RpcCallMeta(requestId, requestId), cancellationToken).ConfigureAwait(false);

            scannedCount += listReply.Items.Count;
            var match = listReply.Items.FirstOrDefault(item => item.InvoiceId == invoiceId);
            if (match is not null)
            {
                return match.OrderReference;
            }

            if (listReply.Items.Count < ScanPageSize)
            {
                // Genuinely exhausted: billing.invoice.list had nothing
                // left to page through. Honest to say "not found" here.
                throw new InvoiceNotFoundError(invoiceId);
            }
        }

        // The scan bound was reached while the LAST page was still full —
        // there may be more invoices beyond it. Honest to say "not found
        // within the search window", dishonest to say "not found".
        throw new InvoiceScanBudgetExceededError(invoiceId, scannedCount);
    }
}
