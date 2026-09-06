using OrderToCash.Billing.Domain.Errors;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// The closed rendering of `domain-model.md` §5.3's invoice state machine —
/// design.md §3.2, ledger row <c>L4</c>. `paidAt` exists **iff** the status
/// is `paid` (invariant <b>B9</b>), and this hierarchy makes the disagreeing
/// case unrepresentable rather than checked: <see cref="Invoice"/> holds
/// exactly ONE field of this type, and <see cref="Invoice.Status"/>/
/// <see cref="Invoice.PaidAt"/> are read-only projections of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>readonly record struct InvoiceState</c> with a private
/// constructor and factory methods — the rendering that looks the most
/// idiomatic.</b> C# gives every struct a <c>default</c> value that no
/// constructor can prevent: <c>default(InvoiceState)</c> would have a null
/// discriminator and a null <c>PaidAt</c>, satisfying NEITHER case, and it is
/// silently reachable through an uninitialised field, an array allocation
/// (<c>new InvoiceState[3]</c>), or a <c>default!</c> in a careless mapper.
/// #7's TypeScript union had no such zero value — a struct always does. That
/// is exactly the shape the ported-idiom ledger exists for: a rendering that
/// satisfies the requirement's WORDS while silently dropping the property
/// that made it correct.
/// </para>
/// <para>
/// <b>Why plain classes, not <c>abstract record</c>/<c>sealed record</c> —
/// found only by writing `BI23`'s own guard, not by inspection.</b> A
/// non-sealed record's COMPILER-SYNTHESISED copy constructor (the one that
/// backs `with` and record equality) is REQUIRED by the language to be at
/// least <c>protected</c> — <c>private protected</c> triggers
/// <c>CS8878</c>, a compile error, because the language does not permit a
/// narrower one. <c>protected</c> is reachable from ANY derived type
/// regardless of assembly, which is exactly the second, wider-than-intended
/// constructor this hierarchy exists to rule out: any assembly that already
/// holds a leaked <see cref="InvoiceState"/> instance (for example, through
/// <see cref="InvoiceSnapshot.State"/>) could derive a third case from it
/// via <c>base(original)</c> alone, never touching the tightened
/// parameterless constructor at all. Plain classes have no such
/// compiler-synthesised member — the explicit <c>private protected</c>
/// constructor below is the ONLY constructor <see cref="InvoiceState"/>
/// has, full stop, and every reachable instance is one of exactly the two
/// nested sealed cases below. This is a recorded deviation from
/// design.md §3.2's literal `abstract record` sketch, made by the arming
/// step it names as `BI23`'s guard — the intended PROPERTY (closure) is
/// unchanged; only the syntax that achieves it is not the one first sketched.
/// </para>
/// </remarks>
public abstract class InvoiceState
{
    // Unreachable outside OrderToCash.Billing, and the ONLY constructor
    // this type has — no compiler-synthesised copy constructor exists for
    // a plain class, so there is no second, wider constructor to worry
    // about (see the remarks above).
    private protected InvoiceState()
    {
    }

    /// <summary>The store-facing projection: <see langword="null"/> for <see cref="Issued"/>, the payment instant for <see cref="Paid"/>. Never independently settable.</summary>
    public abstract DateTimeOffset? PaidAtOrNull { get; }

    /// <summary>The invoice was issued and has not yet been paid. No <c>PaidAt</c> to set — the other half of B9's property.</summary>
    public sealed class Issued : InvoiceState
    {
        public override DateTimeOffset? PaidAtOrNull => null;
    }

    /// <summary>The invoice has been paid. <see cref="PaidAt"/> cannot be omitted — a <see cref="Paid"/> value without an instant does not compile.</summary>
    public sealed class Paid(DateTimeOffset paidAt) : InvoiceState
    {
        public DateTimeOffset PaidAt { get; } = paidAt;

        public override DateTimeOffset? PaidAtOrNull => PaidAt;
    }
}

/// <summary>
/// The token map between <see cref="InvoiceState"/> and the store's two
/// independent columns (`status`, `paid_at`) — the ONE place the closed
/// hierarchy meets a plain string. `BI27`: parsing is ORDINAL and loud
/// (raises <see cref="UnknownInvoiceStatusError"/> outside the closed set
/// rather than coercing), and writing emits only the lower-case contract
/// tokens, so MS-SQL's case-insensitive column collation and the domain's
/// ordinal comparison can never disagree about which invoices a filter
/// selects (ledger `L5`'s sibling).
/// </summary>
public static class InvoiceStatuses
{
    public const string IssuedToken = "issued";
    public const string PaidToken = "paid";

    /// <summary>Projects an <see cref="InvoiceState"/> to its lower-case contract token — the only direction that can never disagree with <paramref name="state"/>, because it is derived from the same closed set the parser accepts.</summary>
    public static string ToToken(InvoiceState state) => state switch
    {
        InvoiceState.Issued => IssuedToken,
        InvoiceState.Paid => PaidToken,
        _ => throw new UnknownInvoiceStatusError(state?.GetType().FullName ?? "<null>"),
    };

    /// <summary>
    /// Parses a stored `status` token plus its `paid_at` value into an
    /// <see cref="InvoiceState"/>. Ordinal comparison, deliberately: MS-SQL's
    /// default collation is case-insensitive and would match `'Paid'`, which
    /// this parser must not. Raises <see cref="UnknownInvoiceStatusError"/>
    /// outside the closed set — never a default value.
    /// </summary>
    /// <remarks>
    /// Does NOT check `status`/`paid_at` agreement — that is <see cref="Invoice.Reconstitute"/>'s
    /// job (`InvalidInvoiceSnapshotError`, `BI10`), because it is an
    /// aggregate invariant, not a token-mapping concern. A caller with a
    /// `paid` status and a null `paidAt` gets a `Paid` state defaulted to
    /// <see cref="DateTimeOffset.MinValue"/> here so the disagreement can be
    /// DETECTED one layer up rather than swallowed here.
    /// </remarks>
    public static InvoiceState Parse(string status, DateTimeOffset? paidAt) => status switch
    {
        // A C# string switch pattern is compiled to an ordinal comparison —
        // never the current culture's collation — which is exactly the
        // property this parser exists to guarantee (BI27).
        IssuedToken => new InvoiceState.Issued(),
        PaidToken => new InvoiceState.Paid(paidAt ?? DateTimeOffset.MinValue),
        _ => throw new UnknownInvoiceStatusError(status),
    };
}
