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
    /// reverse-order-of-acquisition compensation, SA-4 (the human-gated
    /// shared-spec amendment ruled 2026-09-11; <c>saga.md</c> §4.3): when an
    /// operator cancels an order that is <c>credit_approved</c>/
    /// <c>confirmed</c>, <c>stock.release</c> — the CONTESTED resource,
    /// arbitrated by Fulfillment's one lock against a despatch already
    /// requested — is released FIRST; <c>credit.release</c> follows SECOND,
    /// as a <c>CommandAfter</c> a fact-driven <see cref="SagaStepTable"/>
    /// row DOES name (its <c>stock.released.v1</c> row's
    /// <c>credit_approved</c>/<c>confirmed</c> variants, <c>:224-225</c>).
    /// <c>CancelOrderCommandHandler</c> never enqueues this command
    /// directly — it enqueues only <see cref="StockRelease"/>
    /// (<c>CancelOrderCommandHandler.cs:215</c>); every
    /// <see cref="CreditRelease"/> row is enqueued from a FACT-DRIVEN path
    /// (<see cref="SagaFactHandler"/>'s ordinary Advance above, or its
    /// late-approval branch), over the SAME durable mechanism every other
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
