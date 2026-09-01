namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.order_number_sequences` — a single-row
/// technical counter (`id = 1`), incremented under a row lock to allocate
/// `ORD-######` references safely under concurrency (Databases doc §4.2,
/// §3). Unlike every business table, its identity is a well-known small
/// integer, not a domain-generated `UniqueId`: the whole point of `id = 1`
/// is that the allocator can address the one row without a lookup.
/// </summary>
public sealed class OrderNumberSequence
{
    public int Id { get; set; }

    /// <summary>
    /// `int`, per Databases doc §4.2 verbatim: "single-row counter (`id = 1`,
    /// `next_value int`)"; #7's `order-number-sequences.schema.ts:20` is
    /// also `int`. Deliberately not widened to `long`/`bigint` — see review
    /// D2: a widened counter is a spec amendment, not a translation, and
    /// features 10 and 11 are told to copy this table's pattern for
    /// `despatch_number_sequences`/`invoice_number_sequences`.
    /// </summary>
    public int NextValue { get; set; }
}
