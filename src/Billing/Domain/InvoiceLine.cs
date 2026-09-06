using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// One invoiced line — `invoice_items`' domain shape (design.md §3.3),
/// mirroring the child-entity discipline <see cref="Billing.Domain.CreditLedgerEntry"/>
/// already establishes. Lines are snapshotted at issue and never change
/// after that (the <c>OrderLine</c> precedent) — there is no mutator of any
/// kind.
/// </summary>
public sealed class InvoiceLine : Entity
{
    private InvoiceLine(UniqueId id, string productCode, Quantity units, Money unitPrice)
        : base(id)
    {
        ProductCode = productCode;
        Units = units;
        UnitPrice = unitPrice;
    }

    public string ProductCode { get; }

    public Quantity Units { get; }

    public Money UnitPrice { get; }

    /// <summary>The only arithmetic a line performs — <c>unitPrice × units</c>, `checked` via <see cref="Money.Multiply"/>.</summary>
    public Money LineTotal => UnitPrice.Multiply(Units);

    public static InvoiceLine Create(UniqueId id, string productCode, Quantity units, Money unitPrice) =>
        new(id, productCode, units, unitPrice);

    /// <summary>Restores a persisted row — no re-validation beyond what <see cref="Create"/> already performed, because a persisted line was valid when it was written and there is nothing on this type that could have changed it since.</summary>
    public static InvoiceLine Reconstitute(InvoiceLineSnapshot snapshot) =>
        new(snapshot.Id, snapshot.ProductCode, snapshot.Units, snapshot.UnitPrice);

    public InvoiceLineSnapshot ToSnapshot() => new(Id, ProductCode, Units, UnitPrice);
}
