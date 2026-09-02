using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// A child entity of <see cref="Order"/>, with identity within the
/// aggregate but no life of its own (specs/shared/domain-model.md §3.1). A
/// <see langword="sealed class"/>, not a record: two lines with the same
/// product and price are not the same line, and record value equality would
/// silently collapse them in a set.
/// </summary>
/// <remarks>
/// <see cref="ProductCode"/> and <see cref="Description"/> are true
/// constructor-only invariants — changing the product is removing a line
/// and adding another (design.md §5.1). <see cref="Quantity"/>,
/// <see cref="UnitPrice"/> and <see cref="LineDiscount"/> are settable only
/// through the aggregate: the constructor is <see langword="internal"/>, so
/// only code inside <c>Orders.csproj</c> can create an instance, and
/// <c>Order.ChangeLine</c> effects a change by replacing the candidate
/// list's entry with a fresh <see cref="OrderLine"/> carrying the same
/// <see cref="Entity.Id"/> rather than mutating one in place — which keeps
/// every property truly immutable after construction and needs no separate
/// internal mutator.
/// </remarks>
public sealed class OrderLine : Entity
{
    internal OrderLine(UniqueId id, string productCode, string? description, Quantity quantity, Money unitPrice, Money lineDiscount)
        : base(id)
    {
        ProductCode = productCode;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineDiscount = lineDiscount;
    }

    public string ProductCode { get; }

    public string? Description { get; }

    public Quantity Quantity { get; }

    public Money UnitPrice { get; }

    public Money LineDiscount { get; }
}
