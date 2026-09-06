using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>One persisted `invoice_items` row's plain shape — the input <see cref="InvoiceLine.Reconstitute"/> builds from, mirroring <c>CreditLedgerEntrySnapshot</c>.</summary>
public sealed record InvoiceLineSnapshot(UniqueId Id, string ProductCode, Quantity Units, Money UnitPrice);

/// <summary>
/// One persisted `invoices` row's plain shape, plus its lines — what
/// <see cref="Invoice.Reconstitute"/> builds the aggregate from. Carries the
/// STORED <c>status</c>/<c>paidAt</c> pair as already-parsed
/// <see cref="InvoiceState"/> rather than the raw column pair, so the
/// row-mapper (the one place the two representations meet, design.md §6.3)
/// is the only code that ever calls <see cref="InvoiceStatuses.Parse"/>.
/// </summary>
public sealed record InvoiceSnapshot(
    UniqueId Id,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    Money Amount,
    Money Discount,
    Money TotalAmount,
    InvoiceState State,
    IReadOnlyList<InvoiceLineSnapshot> Lines);
