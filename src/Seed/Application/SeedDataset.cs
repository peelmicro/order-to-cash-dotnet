using OrderToCash.Seed.Domain.Sagas;
using Data = OrderToCash.Seed.Domain.Data;

namespace OrderToCash.Seed.Application;

/// <summary>
/// A single read-only view over the whole deterministic dataset this seed
/// writes — the use-case-level surface Infrastructure writers and tests
/// consume, so nothing outside <c>Domain/</c> needs to know the individual
/// builder classes (<c>Domain.Data.Currencies</c>, <c>Domain.Data.Products</c>, …)
/// exist. Every list here is the SAME instance <c>Domain/</c> builds once
/// at class-init time — this type adds no computation of its own.
/// </summary>
public static class SeedDataset
{
    public static IReadOnlyList<Data.CurrencySeed> Currencies => Data.Currencies.All;

    public static IReadOnlyList<Data.ProductSeed> Products => Data.Products.All;

    public static IReadOnlyList<Data.RetailerSeed> Retailers => Data.Retailers.All;

    public static IReadOnlyList<Data.CompanySeed> Companies => Data.Companies.All;

    public static IReadOnlyList<Data.CreditSeed> Credits => Data.Credits.All;

    public static IReadOnlyList<Data.StockSeed> Stock => Data.StockCatalog.All;

    public static IReadOnlyList<OrderSagaFixture> Sagas => SagaFixtures.All;

    public static IReadOnlyList<OrderSagaFixture> CompletedSagas => SagaFixtures.Completed;

    public static IReadOnlyList<OrderSagaFixture> CancelledSagas => SagaFixtures.Cancelled;
}
