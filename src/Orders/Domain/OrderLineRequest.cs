using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// The per-line input to <see cref="Order.Place"/> — the same four
/// caller-supplied fields <c>Order.AddLine</c> takes, bundled so
/// <see cref="Order.Place"/> can accept every line at once
/// (design.md §5.1's <c>AddLine</c> shape). Not part of design.md's file
/// layout by name; introduced because <see cref="OrderLine"/>'s constructor
/// is deliberately <see langword="internal"/> (see its own remarks) and
/// <see cref="Order.Place"/> is a <see langword="public"/> entry point a
/// command handler outside this assembly must be able to call with raw line
/// data.
/// </summary>
public readonly record struct OrderLineRequest(
    string ProductCode,
    string? Description,
    Quantity Quantity,
    Money UnitPrice,
    Money LineDiscount);
