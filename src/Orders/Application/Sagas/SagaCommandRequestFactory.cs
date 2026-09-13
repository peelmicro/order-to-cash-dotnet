using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// Builds the full typed RPC request for an owed <see cref="SagaCommandKind"/>
/// from the loaded, already-transitioned aggregate (design.md §6.3: "the
/// full typed request, serialised through RpcJson at enqueue time from the
/// loaded aggregate"), and serialises it — the one place a saga command's
/// wire body is assembled, so <see cref="SagaFactHandler"/> stays a plain
/// orchestration of ports. Takes <see cref="IRpcRequestSerializer"/> rather
/// than calling <c>Infrastructure.Messaging.Rpc.RpcJson</c> directly (feature
/// 76, <c>application_layer_depends_on_infrastructure_unguarded</c>) — the
/// port that lets this Application class serialise without an Infrastructure
/// dependency; DI supplies the one real implementation,
/// <c>RpcJsonRequestSerializer</c>, which composes <c>RpcJson</c> verbatim.
/// </summary>
public sealed class SagaCommandRequestFactory(IRpcRequestSerializer serializer)
{
    public string BuildJson(SagaCommandKind command, Domain.Order order) => command switch
    {
        SagaCommandKind.StockReserve => RpcJsonString(BuildStockReserve(order)),
        SagaCommandKind.StockRelease => throw new InvalidOperationException(
            "SagaCommandKind.StockRelease's reason is contextual — R27's credit-rejection compensation always uses " +
            "'credit_rejected'. Call BuildStockReleaseJson(order, triggeringFactEventType) instead of this generic " +
            "overload for this one command."),
        SagaCommandKind.DespatchCreate => RpcJsonString(new DespatchCreateRequestPayload(order.OrderReference.Value)),
        SagaCommandKind.CreditHold => RpcJsonString(BuildCreditHold(order)),
        SagaCommandKind.InvoiceIssue => RpcJsonString(BuildInvoiceIssue(order)),
        SagaCommandKind.CreditRelease => RpcJsonString(BuildCreditRelease(order)),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unrecognised SagaCommandKind member."),
    };

    /// <summary>
    /// SA-4 — <see cref="SagaCommandKind.StockRelease"/> is now owed by
    /// exactly ONE fact-driven step: <c>credit.rejected.v1</c>'s Advance
    /// (R27, reason <c>credit_rejected</c>). Before SA-4 a second step —
    /// the operator-cancel compensation's <c>credit.released.v1</c>
    /// variant — owed it too (reason <c>order_cancelled</c>); SA-4 releases
    /// stock FIRST instead (§4.3), so that variant no longer exists.
    /// <see cref="StockReleaseReasonFor"/> still derives the reason from
    /// the TRIGGERING fact's own <c>eventType</c>, never inferred any other
    /// way — kept parametric rather than hard-coded so a future step that
    /// also owes <c>stock.release</c> is a one-line addition, not a second
    /// builder. <c>CancelOrderCommandHandler</c>'s own DIRECT enqueue (both
    /// the <c>stock_reserved</c> and, since SA-4, the <c>credit_approved</c>/
    /// <c>confirmed</c> branch) has no triggering fact at all (it is
    /// RPC-triggered) and does not call this method — it builds its
    /// <see cref="StockReleaseRequestPayload"/> inline instead.
    /// </summary>
    public string BuildStockReleaseJson(Domain.Order order, string triggeringFactEventType) =>
        RpcJsonString(new StockReleaseRequestPayload(order.OrderReference.Value, StockReleaseReasonFor(triggeringFactEventType)));

    public static string StockReleaseReasonFor(string triggeringFactEventType) => triggeringFactEventType switch
    {
        "credit.rejected.v1" => "credit_rejected",
        _ => throw new ArgumentOutOfRangeException(
            nameof(triggeringFactEventType),
            triggeringFactEventType,
            "stock.release is only ever owed by credit.rejected.v1 (R27) — no other fact type's step names " +
            "SagaCommandKind.StockRelease as CommandAfter (SA-4 retired credit.released.v1's own former variant)."),
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
        new CreditMoney(order.TotalAmount.MinorUnits, order.Currency));

    private static InvoiceIssueRequestPayload BuildInvoiceIssue(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        order.Currency,
        [.. order.Lines.Select(line => new Contracts.Facts.InvoiceLine(line.ProductCode, line.Quantity.Value, line.UnitPrice.MinorUnits))],
        order.InitialDiscount.MinorUnits);

    /// <summary>
    /// SA-4 — <see cref="SagaCommandKind.CreditRelease"/> is now owed by TWO
    /// step-table rows too: <c>stock.released.v1</c>'s <c>credit_approved</c>/
    /// <c>confirmed</c> Advance variants (the contested resource released
    /// first, per saga.md §4.3), and the late-<c>credit.approved.v1</c>
    /// unwind <see cref="SagaFactHandler"/> handles directly. Both reach this
    /// generic overload through <c>SagaFactHandler.HandleAsync</c>'s own
    /// <c>owedCommand</c>/direct-enqueue paths — <c>CreditRelease</c> needs
    /// no reason-aware builder the way <see cref="SagaCommandKind.StockRelease"/>
    /// does, because the <c>credit.release</c> RPC has no <c>reason</c> field
    /// at all (Billing's <c>CreditReleaseService</c> always releases with
    /// <c>order_cancelled</c>). This builder exists here
    /// (rather than only inline at each call site) so BuildJson stays the one
    /// exhaustive switch over every <see cref="SagaCommandKind"/> member,
    /// matching the other five.
    /// </summary>
    private static CreditReleaseRequestPayload BuildCreditRelease(Domain.Order order) =>
        new(order.OrderReference.Value, order.RetailerCode, order.CompanyCode);

    private string RpcJsonString<T>(T payload) => System.Text.Encoding.UTF8.GetString(serializer.Serialize(payload));
}
