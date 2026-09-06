using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>The time and causation this aggregate stamps a fact with — supplied, never pulled (no <c>IClock</c> reference here, design.md §3.1).</summary>
public readonly record struct CreditContext(DateTimeOffset OccurredAt, UniqueId CausationId);

/// <summary>The order-scoped input to <see cref="BuyerCredit.EvaluateHold"/>/<see cref="BuyerCredit.Approve"/>/<see cref="BuyerCredit.Refuse"/>. <see cref="CorrelationId"/> is the ORDER id (from <c>x-correlation-id</c>), distinct from <see cref="OrderReference"/>, the human-readable business reference.</summary>
public sealed record HoldRequest(OrderNumber OrderReference, Money Amount, UniqueId CorrelationId);

/// <summary>
/// The aggregate root for one credit line — one row of <c>credits</c> plus
/// its append-only <c>credit_items</c> ledger (specs/shared/domain-model.md
/// §5.1). Invariant <b>B1</b> (<c>Σholds + Σexposure ≤ creditLimit</c>)
/// lives here, not in the schema (design.md §3.1): breaking it produces a
/// <c>credit.rejected.v1</c> FACT, never a raw provider error.
/// </summary>
/// <remarks>
/// Like <c>StockItem</c> and its <c>ReservedUnits</c>, this aggregate
/// PRESERVES <b>B1</b> rather than recomputing it from everything it can
/// see: it is reconstituted with the <c>credits</c> row, the whole line's
/// committed-exposure scalar (one number, computed in SQL), and the
/// complete entry list of the ONE order the current command names. It never
/// loads the whole ledger.
/// </remarks>
public sealed class BuyerCredit : AggregateRoot
{
    private readonly List<CreditLedgerEntry> _entries = [];
    private readonly List<CreditLedgerEntry> _appendedEntries = [];
    private Money _committedExposure;

    private BuyerCredit(UniqueId id, string code, string retailerCode, string companyCode, Money creditLimit, long committedExposureMinorUnits)
        : base(id)
    {
        Code = code;
        RetailerCode = retailerCode;
        CompanyCode = companyCode;
        CreditLimit = creditLimit;
        _committedExposure = new Money(committedExposureMinorUnits, creditLimit.Currency);
    }

    public string Code { get; }

    public string RetailerCode { get; }

    public string CompanyCode { get; }

    public Money CreditLimit { get; }

    /// <summary>`BC5` — <c>creditLimit − committedExposure</c>, recomputed on every read, never cached or stored.</summary>
    public Money AvailableCredit => CreditLimit.Subtract(_committedExposure);

    /// <summary>`BC6` — the per-order split, folded through the SAME <see cref="CreditExposure.Summarise"/> the read side uses.</summary>
    public LedgerSummary Summary => CreditExposure.Summarise([.. _entries.Select(e => e.ToSnapshot())]);

    /// <summary>What the repository INSERTs — the loaded entries are never re-written (`B2`).</summary>
    public IReadOnlyList<CreditLedgerEntry> AppendedEntries => _appendedEntries;

    /// <summary>
    /// Restores a persisted line. Refuses a snapshot whose committed
    /// exposure already exceeds the credit limit (`B1`'s OUTER bound — the
    /// arithmetic itself is guarded separately, `BC30`) or whose entry
    /// currency differs from the line's (`B3`).
    /// </summary>
    public static BuyerCredit Reconstitute(BuyerCreditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.CommittedExposureMinorUnits > snapshot.CreditLimit.MinorUnits)
        {
            throw new InvalidBuyerCreditSnapshotError(
                $"credit line '{snapshot.Code}': committed exposure ({snapshot.CommittedExposureMinorUnits}) already exceeds the credit limit ({snapshot.CreditLimit.MinorUnits}) — invariant B1.");
        }

        foreach (var entrySnapshot in snapshot.Entries)
        {
            if (!string.Equals(entrySnapshot.Amount.Currency, snapshot.CreditLimit.Currency, StringComparison.Ordinal))
            {
                throw new InvalidBuyerCreditSnapshotError(
                    $"credit line '{snapshot.Code}': entry '{entrySnapshot.Id}' currency '{entrySnapshot.Amount.Currency}' differs from the line's currency '{snapshot.CreditLimit.Currency}' — invariant B3.");
            }
        }

        var credit = new BuyerCredit(snapshot.Id, snapshot.Code, snapshot.RetailerCode, snapshot.CompanyCode, snapshot.CreditLimit, snapshot.CommittedExposureMinorUnits);

        foreach (var entrySnapshot in snapshot.Entries)
        {
            credit._entries.Add(CreditLedgerEntry.Reconstitute(entrySnapshot));
        }

        return credit;
    }

    /// <summary>
    /// PURE. No mutation, no event, no port. The single decision function;
    /// the application layer consults the credit-decision port ONLY on
    /// <see cref="HoldEvaluation.Fits"/> (`BC13`, §6.1). The case order is
    /// FIXED by `BC26` and asserted, not incidental:
    /// <see cref="HoldEvaluation.AlreadyHeld"/> &gt;
    /// <see cref="HoldEvaluation.CurrencyMismatch"/> &gt;
    /// <see cref="HoldEvaluation.OverLimit"/>.
    /// </summary>
    public HoldEvaluation EvaluateHold(HoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existingHold = FindHoldEntry(request.OrderReference);
        if (existingHold is not null)
        {
            return new HoldEvaluation.AlreadyHeld(existingHold.Amount);
        }

        if (!string.Equals(request.Amount.Currency, CreditLimit.Currency, StringComparison.Ordinal))
        {
            return new HoldEvaluation.CurrencyMismatch(CreditLimit.Currency);
        }

        if (request.Amount.MinorUnits > AvailableCredit.MinorUnits)
        {
            return new HoldEvaluation.OverLimit(AvailableCredit);
        }

        return new HoldEvaluation.Fits();
    }

    /// <summary>
    /// Appends ONE <c>hold</c> entry and exactly one <c>credit.approved.v1</c>
    /// whose <c>availableCreditAfter</c> is recomputed WITH the new entry
    /// (`BC10`). Throws <see cref="CreditLimitExceededError"/> unless
    /// <see cref="EvaluateHold"/> would answer <see cref="HoldEvaluation.Fits"/> —
    /// a caller cannot bypass `B1` by skipping the evaluation.
    /// </summary>
    public CreditLedgerEntry Approve(HoldRequest request, CreditContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(newId);

        if (EvaluateHold(request) is not HoldEvaluation.Fits)
        {
            throw new CreditLimitExceededError(request.Amount.MinorUnits, AvailableCredit.MinorUnits);
        }

        var entry = CreditLedgerEntry.Create(newId(), request.OrderReference, request.Amount, CreditEntryType.Hold, ctx.OccurredAt);
        AppendEntry(entry);
        _committedExposure = _committedExposure.Add(entry.Amount);

        Raise(new CreditApproved(
            EventId: newId(),
            AggregateId: Id,
            CorrelationId: request.CorrelationId,
            CausationId: ctx.CausationId,
            OccurredAt: ctx.OccurredAt,
            OrderReference: request.OrderReference,
            RetailerCode: RetailerCode,
            CompanyCode: CompanyCode,
            CreditCode: Code,
            HeldAmount: request.Amount,
            AvailableCreditAfter: AvailableCredit));

        return entry;
    }

    /// <summary>
    /// Appends NO entry and exactly one <c>credit.rejected.v1</c> carrying
    /// <paramref name="reason"/> (`R39`, `B1`). Throws
    /// <see cref="CreditRefusalMismatchError"/> if <paramref name="reason"/>
    /// is <see cref="CreditRejectionReason.OverLimit"/> while the amount
    /// actually fits — a refusal must not lie about why (`BC14`). This is
    /// the ONE builder, the ONE call site, for <c>credit.rejected.v1</c>: a
    /// genuine over-limit refusal and an adapter refusal run through these
    /// same lines and differ in exactly one field, <paramref name="reason"/>.
    /// </summary>
    public void Refuse(HoldRequest request, CreditRejectionReason reason, CreditContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(newId);

        if (reason == CreditRejectionReason.OverLimit &&
            string.Equals(request.Amount.Currency, CreditLimit.Currency, StringComparison.Ordinal) &&
            request.Amount.MinorUnits <= AvailableCredit.MinorUnits)
        {
            throw new CreditRefusalMismatchError(request.Amount.MinorUnits, AvailableCredit.MinorUnits);
        }

        Raise(new CreditRejected(
            EventId: newId(),
            AggregateId: Id,
            CorrelationId: request.CorrelationId,
            CausationId: ctx.CausationId,
            OccurredAt: ctx.OccurredAt,
            OrderReference: request.OrderReference,
            RetailerCode: RetailerCode,
            CompanyCode: CompanyCode,
            CreditCode: Code,
            RequestedAmount: request.Amount,
            AvailableCredit: AvailableCredit,
            Reason: reason));
    }

    /// <summary>
    /// Appends ONE <c>release</c> entry for the order's outstanding exposure
    /// and exactly one <c>credit.released.v1</c> with <paramref name="reason"/>.
    /// Returns <see langword="null"/> — no entry, no fact — when the order
    /// has no outstanding exposure (`BC11`, `B5`).
    /// </summary>
    public CreditLedgerEntry? Release(OrderNumber orderReference, CreditReleaseReason reason, UniqueId correlationId, CreditContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(newId);

        var outstanding = OutstandingExposureOf(orderReference);
        if (outstanding <= 0)
        {
            return null;
        }

        var amount = new Money(outstanding, CreditLimit.Currency);
        var entry = CreditLedgerEntry.Create(newId(), orderReference, amount, CreditEntryType.Release, ctx.OccurredAt);

        // B5: a release may never drive exposure(order) below zero. Release
        // always releases EXACTLY the outstanding exposure, so this can
        // never actually trigger through this method — it is the domain's
        // own defence, the same shape ConcurrentReservationChangeError is
        // in Fulfillment, rather than a reachable caller mistake.
        if (outstanding - amount.MinorUnits < 0)
        {
            throw new CreditReleaseUnderflowError(orderReference.Value, outstanding, amount.MinorUnits);
        }

        AppendEntry(entry);
        _committedExposure = _committedExposure.Subtract(entry.Amount);

        Raise(new CreditReleased(
            EventId: newId(),
            AggregateId: Id,
            CorrelationId: correlationId,
            CausationId: ctx.CausationId,
            OccurredAt: ctx.OccurredAt,
            OrderReference: orderReference,
            RetailerCode: RetailerCode,
            CompanyCode: CompanyCode,
            CreditCode: Code,
            ReleasedAmount: amount,
            AvailableCreditAfter: AvailableCredit,
            Reason: reason));

        return entry;
    }

    /// <summary>
    /// Appends ONE <c>consume</c> entry of the order's active hold. Emits
    /// NOTHING — <c>invoice.issued.v1</c> is feature 21's <c>Invoice</c>
    /// fact (`R40`, `BC12`). Throws <see cref="NoActiveHoldError"/> when the
    /// order holds nothing. Ships ready and uncalled in this feature.
    /// </summary>
    public CreditLedgerEntry Consume(OrderNumber orderReference, CreditContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(newId);

        var activeHold = ActiveHoldOf(orderReference);
        if (activeHold <= 0)
        {
            throw new NoActiveHoldError(orderReference.Value);
        }

        var amount = new Money(activeHold, CreditLimit.Currency);
        var entry = CreditLedgerEntry.Create(newId(), orderReference, amount, CreditEntryType.Consume, ctx.OccurredAt);
        AppendEntry(entry);

        // R40: a `consume` entry appears in NEITHER term of `availableCredit`
        // — _committedExposure is deliberately left untouched. This is a
        // structural consequence of CreditExposure.Summarise's formula, not
        // a rule applied here on top of it.
        return entry;
    }

    public BuyerCreditSnapshot ToSnapshot() => new(
        Id,
        Code,
        RetailerCode,
        CompanyCode,
        CreditLimit,
        _committedExposure.MinorUnits,
        [.. _entries.Select(e => e.ToSnapshot())]);

    private void AppendEntry(CreditLedgerEntry entry)
    {
        _appendedEntries.Add(entry);
        _entries.Add(entry);
    }

    private CreditLedgerEntry? FindHoldEntry(OrderNumber orderReference) =>
        _entries.FirstOrDefault(e => e.Type == CreditEntryType.Hold && OrderReferenceEquals(e.OrderReference, orderReference));

    private long OutstandingExposureOf(OrderNumber orderReference)
    {
        var orderExposure = Summary.ByOrder.FirstOrDefault(o => string.Equals(o.OrderReference, orderReference.Value, StringComparison.OrdinalIgnoreCase));
        return orderExposure?.Exposure ?? 0;
    }

    private long ActiveHoldOf(OrderNumber orderReference)
    {
        var orderExposure = Summary.ByOrder.FirstOrDefault(o => string.Equals(o.OrderReference, orderReference.Value, StringComparison.OrdinalIgnoreCase));
        return orderExposure?.ActiveHold ?? 0;
    }

    private static bool OrderReferenceEquals(OrderNumber a, OrderNumber b) =>
        string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
}
