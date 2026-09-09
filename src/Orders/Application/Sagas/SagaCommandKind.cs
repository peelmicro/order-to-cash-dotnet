namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The closed set of five commands the saga may owe after a step (design.md
/// §4.1, §6.3) — mirrors <c>saga_commands.command</c>'s five legal values.
/// </summary>
public enum SagaCommandKind
{
    StockReserve,
    StockRelease,
    DespatchCreate,
    CreditHold,
    InvoiceIssue,

    /// <summary>
    /// The sixth saga command — feature <c>orders_cancel_responder</c>'s
    /// reverse-order-of-acquisition compensation (<c>saga.md</c> §4.3): the
    /// credit hold is released FIRST when an operator cancels an order that
    /// is <c>credit_approved</c>/<c>confirmed</c>, before <c>stock.release</c>
    /// follows. No fact-driven <see cref="SagaStepTable"/> row ever names
    /// this as a <c>CommandAfter</c> — <c>CancelOrderCommandHandler</c>
    /// enqueues it directly, over the SAME durable mechanism every other
    /// saga command uses.
    /// </summary>
    CreditRelease,
}

/// <summary>Maps <see cref="SagaCommandKind"/> to and from its wire/storage token — the <c>saga_commands.command</c> column value and the RPC subject's own vocabulary, following the <c>OrderStatuses</c>/<c>CancellationReasons</c> convention.</summary>
public static class SagaCommandKinds
{
    public static string ToToken(SagaCommandKind kind) => kind switch
    {
        SagaCommandKind.StockReserve => "stock.reserve",
        SagaCommandKind.StockRelease => "stock.release",
        SagaCommandKind.DespatchCreate => "despatch.create",
        SagaCommandKind.CreditHold => "credit.hold",
        SagaCommandKind.InvoiceIssue => "invoice.issue",
        SagaCommandKind.CreditRelease => "credit.release",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognised SagaCommandKind member."),
    };

    public static SagaCommandKind Parse(string? token) => token switch
    {
        "stock.reserve" => SagaCommandKind.StockReserve,
        "stock.release" => SagaCommandKind.StockRelease,
        "despatch.create" => SagaCommandKind.DespatchCreate,
        "credit.hold" => SagaCommandKind.CreditHold,
        "invoice.issue" => SagaCommandKind.InvoiceIssue,
        "credit.release" => SagaCommandKind.CreditRelease,
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unrecognised saga_commands.command token."),
    };
}
