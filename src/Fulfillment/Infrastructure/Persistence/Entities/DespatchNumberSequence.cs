namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.despatch_number_sequences` — a
/// single-row technical counter (`id = 1`), incremented under a row lock to
/// allocate `DES-######` references safely under concurrency (Databases doc
/// §5, §3). Same shape and same reasoning as `otc_orders.order_number_sequences`
/// (feature db_orders, review D2): its identity is a well-known small
/// integer, not a domain-generated `UniqueId`.
/// </summary>
public sealed class DespatchNumberSequence
{
    public int Id { get; set; }

    /// <summary>
    /// `int`, per Databases doc §5 ("single-row counter for `DES-######`")
    /// and #7's `despatch_number_sequences` table (`next_value int NOT
    /// NULL`, `apps/fulfillment/drizzle/0002_despatch_number_sequence_and_order_reference_unique.sql:12`).
    /// Deliberately not widened to `long`/`bigint` — feature db_orders's
    /// review (D2) named this exact table as one of the two about to copy
    /// that mistake if `order_number_sequences.next_value` were left
    /// `bigint`; it was fixed there first, and this table follows the fixed
    /// pattern, not the original one.
    /// </summary>
    public int NextValue { get; set; }
}
