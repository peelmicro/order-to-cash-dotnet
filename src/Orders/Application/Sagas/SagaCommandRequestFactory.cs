using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// Builds the full typed RPC request for an owed <see cref="SagaCommandKind"/>
/// from the loaded, already-transitioned aggregate (design.md §6.3: "the
/// full typed request, serialised through RpcJson at enqueue time from the
/// loaded aggregate"), and serialises it — the one place a saga command's
/// wire body is assembled, so <see cref="SagaFactHandler"/> stays a plain
/// orchestration of ports.
/// </summary>
public static class SagaCommandRequestFactory
{
    public static string BuildJson(SagaCommandKind command, Domain.Order order) => command switch
    {
        SagaCommandKind.StockReserve => RpcJsonString(BuildStockReserve(order)),
        SagaCommandKind.StockRelease => throw new InvalidOperationException(
            "SagaCommandKind.StockRelease's reason is contextual — R27's credit-rejection compensation always uses " +
            "'credit_rejected', feature orders_cancel_responder's operator-cancel compensation always uses " +
            "'order_cancelled'. Call BuildStockReleaseJson(order, triggeringFactEventType) instead of this generic " +
            "overload for this one command."),
        SagaCommandKind.DespatchCreate => RpcJsonString(new DespatchCreateRequestPayload(order.OrderReference.Value)),
        SagaCommandKind.CreditHold => RpcJsonString(BuildCreditHold(order)),
        SagaCommandKind.InvoiceIssue => RpcJsonString(BuildInvoiceIssue(order)),
        SagaCommandKind.CreditRelease => RpcJsonString(BuildCreditRelease(order)),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unrecognised SagaCommandKind member."),
    };

    /// <summary>
    /// <see cref="SagaCommandKind.StockRelease"/> is owed by TWO distinct
    /// fact-driven steps with two distinct reasons (design.md §6.1, feature
    /// <c>orders_cancel_responder</c>'s ported-idiom ledger): the
    /// <c>credit.rejected.v1</c> step (R27, reason <c>credit_rejected</c>)
    /// and the operator-cancel compensation's <c>credit.released.v1</c>
    /// variant (reason <c>order_cancelled</c>) — <see cref="StockReleaseReasonFor"/>
    /// derives which from the TRIGGERING fact's own <c>eventType</c>, never
    /// inferred any other way. <c>CancelOrderCommandHandler</c>'s own DIRECT
    /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
    /// all (it is RPC-triggered) and does not call this method — it builds
    /// its <see cref="StockReleaseRequestPayload"/> inline instead.
    /// </summary>
    public static string BuildStockReleaseJson(Domain.Order order, string triggeringFactEventType) =>
        RpcJsonString(new StockReleaseRequestPayload(order.OrderReference.Value, StockReleaseReasonFor(triggeringFactEventType)));

    public static string StockReleaseReasonFor(string triggeringFactEventType) => triggeringFactEventType switch
    {
        "credit.rejected.v1" => "credit_rejected",
        "credit.released.v1" => "order_cancelled",
        _ => throw new ArgumentOutOfRangeException(
            nameof(triggeringFactEventType),
            triggeringFactEventType,
            "stock.release is only ever owed by credit.rejected.v1 (R27) or credit.released.v1's operator-cancel " +
            "compensation variant — no other fact type's step names SagaCommandKind.StockRelease as CommandAfter."),
    };

    private static StockReserveRequestPayload BuildStockReserve(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        [.. order.Lines.Select(line => new StockReserveRequestLine(line.ProductCode, line.Quantity.Value))]);

    private static CreditHoldRequestPayload BuildCreditHold(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        new SagaMoney(order.TotalAmount.MinorUnits, order.Currency));

    private static InvoiceIssueRequestPayload BuildInvoiceIssue(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        order.Currency,
        [.. order.Lines.Select(line => new Contracts.Facts.InvoiceLine(line.ProductCode, line.Quantity.Value, line.UnitPrice.MinorUnits))],
        order.InitialDiscount.MinorUnits);

    /// <summary>
    /// The ONLY fact-driven producer of <see cref="SagaCommandKind.CreditRelease"/>
    /// would be a step-table row — there is none: this command is owed
    /// exclusively by <c>CancelOrderCommandHandler</c>'s own direct enqueue
    /// for the <c>credit_approved</c>/<c>confirmed</c> branch. This builder
    /// exists here (rather than only inline in the handler) so BuildJson
    /// stays the one exhaustive switch over every <see cref="SagaCommandKind"/>
    /// member, matching the other five.
    /// </summary>
    private static CreditReleaseRequestPayload BuildCreditRelease(Domain.Order order) =>
        new(order.OrderReference.Value, order.RetailerCode, order.CompanyCode);

    private static string RpcJsonString<T>(T payload) => System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));
}
