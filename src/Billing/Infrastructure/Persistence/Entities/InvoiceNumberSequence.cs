namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.invoice_number_sequences` — a single-row
/// technical counter (`id = 1`), incremented under a row lock to allocate
/// `INV-######` references safely under concurrency (Databases doc §6, §3).
/// Same shape and same reasoning as `otc_orders.order_number_sequences` and
/// `otc_fulfillment.despatch_number_sequences`: its identity is a
/// well-known small integer, not a domain-generated `UniqueId`.
/// </summary>
public sealed class InvoiceNumberSequence
{
    public int Id { get; set; }

    /// <summary>
    /// `int`, per Databases doc §6 ("single-row counter for `INV-######`")
    /// and #7's `invoice_number_sequences` table (`next_value int NOT
    /// NULL`, `apps/billing/drizzle/0002_invoice_sequences_and_order_uniqueness.sql`).
    /// This is the third and last of the three sequence tables named in
    /// feature db_orders's review (D2): deliberately not widened to
    /// `long`/`bigint`, following `despatch_number_sequences`'s already-fixed
    /// pattern rather than `order_number_sequences`'s original mistake.
    /// </summary>
    public int NextValue { get; set; }
}
