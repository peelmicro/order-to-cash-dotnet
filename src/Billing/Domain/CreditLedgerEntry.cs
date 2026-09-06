using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// One append-only movement of a credit line's ledger — domain-model.md
/// §5.1's <c>credit_items</c> row, one <c>CreditLedgerEntry</c> per
/// <c>hold</c>/<c>consume</c>/<c>release</c>. Exposes NO setter and NO
/// mutating method of any kind — invariant <b>B2</b> (append-only) is
/// enforced by shape, not by a rule the caller must remember: there is
/// simply nothing on this type a caller could call to change an already-
/// appended row.
/// </summary>
public sealed class CreditLedgerEntry : Entity
{
    private CreditLedgerEntry(UniqueId id, OrderNumber orderReference, Money amount, CreditEntryType type, DateTimeOffset entryDate)
        : base(id)
    {
        OrderReference = orderReference;
        Amount = amount;
        Type = type;
        EntryDate = entryDate;
    }

    public OrderNumber OrderReference { get; }

    public Money Amount { get; }

    public CreditEntryType Type { get; }

    public DateTimeOffset EntryDate { get; }

    /// <summary>The only construction site for a brand-new entry — refuses a non-positive amount (B1/B2 depend on every entry being a genuine positive movement).</summary>
    public static CreditLedgerEntry Create(UniqueId id, OrderNumber orderReference, Money amount, CreditEntryType type, DateTimeOffset entryDate)
    {
        if (amount.MinorUnits <= 0)
        {
            throw new InvalidBuyerCreditSnapshotError($"a {CreditEntryTypes.ToToken(type)} entry's amount must be strictly positive; got {amount.MinorUnits}.");
        }

        return new CreditLedgerEntry(id, orderReference, amount, type, entryDate);
    }

    /// <summary>Restores a persisted row — no validation beyond what <see cref="Create"/> already performs, because a persisted row was valid when it was written and B2 forbids anything from having changed it since. Parses <see cref="CreditLedgerEntrySnapshot.OrderReference"/> into the domain-typed <see cref="OrderNumber"/> this entity carries.</summary>
    public static CreditLedgerEntry Reconstitute(CreditLedgerEntrySnapshot snapshot) =>
        new(snapshot.Id, OrderNumber.Parse(snapshot.OrderReference), snapshot.Amount, snapshot.Type, snapshot.EntryDate);

    public CreditLedgerEntrySnapshot ToSnapshot() => new(Id, OrderReference.Value, Amount, Type, EntryDate);
}
