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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognised SagaCommandKind member."),
    };

    public static SagaCommandKind Parse(string? token) => token switch
    {
        "stock.reserve" => SagaCommandKind.StockReserve,
        "stock.release" => SagaCommandKind.StockRelease,
        "despatch.create" => SagaCommandKind.DespatchCreate,
        "credit.hold" => SagaCommandKind.CreditHold,
        "invoice.issue" => SagaCommandKind.InvoiceIssue,
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unrecognised saga_commands.command token."),
    };
}
