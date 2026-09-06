using OrderToCash.Billing.Domain.Errors;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// One order's share of a credit line's outstanding exposure — the split
/// <c>billing.credit.list</c> reports (`BC6`). <see cref="HasHoldEntry"/> is
/// `BC7`'s idempotency predicate: a <c>hold</c> entry was recorded for this
/// order, whatever happened since (an order whose hold was later released
/// still answers <see langword="true"/>).
/// </summary>
public sealed record OrderExposure(
    string OrderReference,
    long Exposure,
    long OpenExposure,
    long ActiveHold,
    bool HasHoldEntry);

/// <summary>The whole-line summary <see cref="CreditExposure.Summarise"/> produces — `BC5`'s two-term sum plus `BC6`'s per-order split.</summary>
public sealed record LedgerSummary(
    IReadOnlyList<OrderExposure> ByOrder,
    long CommittedExposure,
    long ActiveHolds,
    long OpenExposure);

/// <summary>
/// PURE. The single place `BC5` and `BC6` are computed — by the aggregate
/// and by the read side alike (design.md §3.3, the crux). The two-term
/// identity, not <c>domain-model.md</c> §5.1's literal formula, whose
/// <c>"applied to holds"</c> qualifier is not computable and whose naive
/// reading yields a negative <c>openExposure</c> for an order cancelled
/// before invoicing — the case <b>B5</b> forbids.
/// </summary>
public static class CreditExposure
{
    /// <summary>
    /// The whole accumulation runs inside ONE <see langword="checked"/>
    /// region — the per-order hold/release/consume accumulators,
    /// <c>exposure(order)</c>, the committed exposure across orders, and
    /// <c>openExposure</c>/<c>activeHold</c> — so an overflow RAISES rather
    /// than wraps (`BC30`, design.md §3.3, §15 <c>L25</c>). Written as
    /// explicit loops, deliberately NOT <see cref="Enumerable.Sum(IEnumerable{long})"/>:
    /// <c>Sum</c> happens to throw on overflow in the current BCL, but
    /// nothing in its signature promises that, and "the library did it" is
    /// the answer this repository's ported-idiom ledger exists to refuse.
    /// </summary>
    public static LedgerSummary Summarise(IReadOnlyList<CreditLedgerEntrySnapshot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            checked
            {
                var orderReferenceByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var holdByOrder = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var releaseByOrder = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                var consumeByOrder = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    var key = entry.OrderReference;
                    orderReferenceByKey.TryAdd(key, key);

                    switch (entry.Type)
                    {
                        case CreditEntryType.Hold:
                            holdByOrder[key] = holdByOrder.GetValueOrDefault(key) + entry.Amount.MinorUnits;
                            break;
                        case CreditEntryType.Release:
                            releaseByOrder[key] = releaseByOrder.GetValueOrDefault(key) + entry.Amount.MinorUnits;
                            break;
                        case CreditEntryType.Consume:
                            consumeByOrder[key] = consumeByOrder.GetValueOrDefault(key) + entry.Amount.MinorUnits;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(entries), entry.Type, "Unrecognised CreditEntryType member.");
                    }
                }

                var byOrder = new List<OrderExposure>(orderReferenceByKey.Count);
                long committedExposure = 0;
                long activeHolds = 0;
                long openExposure = 0;

                foreach (var key in orderReferenceByKey.Keys)
                {
                    var hold = holdByOrder.GetValueOrDefault(key);
                    var release = releaseByOrder.GetValueOrDefault(key);
                    var consume = consumeByOrder.GetValueOrDefault(key);

                    // exposure(order) = Σ hold(order) − Σ release(order) — B5 keeps this ≥ 0.
                    var exposure = hold - release;

                    // openExposure(order) = min(Σ consume(order), exposure(order)).
                    var orderOpenExposure = Math.Min(consume, exposure);

                    // activeHold(order) = exposure(order) − openExposure(order).
                    var orderActiveHold = exposure - orderOpenExposure;

                    byOrder.Add(new OrderExposure(orderReferenceByKey[key], exposure, orderOpenExposure, orderActiveHold, hold > 0));

                    committedExposure += exposure;
                    activeHolds += orderActiveHold;
                    openExposure += orderOpenExposure;
                }

                return new LedgerSummary(byOrder, committedExposure, activeHolds, openExposure);
            }
        }
        catch (OverflowException ex)
        {
            throw new CreditLedgerOverflowError(ex);
        }
    }
}
