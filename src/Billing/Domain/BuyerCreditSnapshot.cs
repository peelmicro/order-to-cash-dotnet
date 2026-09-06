using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// The plain, framework-free shape a <see cref="CreditLedgerEntry"/> is
/// reconstituted from and produces (design.md §3.2).
/// <see cref="OrderReference"/> is a raw <see cref="string"/>, not
/// <see cref="OrderNumber"/>, deliberately: <see cref="CreditExposure.Summarise"/>
/// folds this shape directly on the READ side (design.md §7.3) without
/// constructing an <see cref="OrderNumber"/> per row, and the grouping
/// comparer (`BC28`) is what stands in for MS-SQL's own case-insensitive
/// collation on this exact string — a concern that belongs at THIS
/// boundary, not inside a validated value object.
/// </summary>
public sealed record CreditLedgerEntrySnapshot(
    UniqueId Id,
    string OrderReference,
    Money Amount,
    CreditEntryType Type,
    DateTimeOffset EntryDate);

/// <summary>
/// The plain, framework-free shape a <see cref="BuyerCredit"/> is
/// reconstituted from and produces (design.md §3.1). <see cref="Entries"/>
/// is scoped to the ONE order the current command names, plus whatever the
/// committed-exposure scalar already summarises — never the line's entire
/// history (§3.1's "What the aggregate holds, honestly").
/// </summary>
public sealed record BuyerCreditSnapshot(
    UniqueId Id,
    string Code,
    string RetailerCode,
    string CompanyCode,
    Money CreditLimit,
    long CommittedExposureMinorUnits,
    IReadOnlyList<CreditLedgerEntrySnapshot> Entries);
