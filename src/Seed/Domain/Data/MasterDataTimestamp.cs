namespace OrderToCash.Seed.Domain.Data;

/// <summary>
/// The single fixed instant every master-data row (currencies, products,
/// retailers, companies, credit lines, stock) is stamped with — ported from
/// #7's <c>data/constants.ts</c>. Distinct from — and safely before — the
/// sample sagas' dates (<see cref="Sagas.SagaFixtures.BaseDate"/>,
/// 2026-06-01), so "master data existed before any order was ever placed"
/// reads true in the seeded history here too.
/// </summary>
public static class MasterDataTimestamp
{
    public static readonly DateTime Value = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
