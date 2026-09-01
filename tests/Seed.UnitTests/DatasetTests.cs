using OrderToCash.Seed.Application;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Seed.UnitTests;

/// <summary>
/// Feature seed_job acceptance: "same currencies, products, retailers,
/// companies, GLNs, credit limits and stock as #7" — the pure, in-process
/// half of that claim (row counts and shapes match #7's own dataset
/// declarations). No container needed: <see cref="SeedDataset"/> is built
/// entirely in memory.
/// </summary>
public sealed class DatasetTests
{
    [Fact]
    public void Three_Currencies_Are_Seeded()
    {
        Assert.Equal(3, SeedDataset.Currencies.Count);
        Assert.Equal(["USD", "EUR", "GBP"], [.. SeedDataset.Currencies.Select(c => c.Code)]);
    }

    [Fact]
    public void At_Least_Ten_Products_Are_Seeded()
    {
        Assert.True(SeedDataset.Products.Count >= 10, $"expected >= 10 products, found {SeedDataset.Products.Count}");
        Assert.Equal(12, SeedDataset.Products.Count);
    }

    [Fact]
    public void Seven_Retailers_Are_Seeded()
    {
        Assert.Equal(7, SeedDataset.Retailers.Count);
    }

    [Fact]
    public void At_Least_Twenty_Companies_Are_Seeded()
    {
        Assert.True(SeedDataset.Companies.Count >= 20, $"expected >= 20 companies, found {SeedDataset.Companies.Count}");
        Assert.Equal(22, SeedDataset.Companies.Count);
    }

    [Fact]
    public void Every_Retailer_Has_A_Credit_Line()
    {
        foreach (var retailer in SeedDataset.Retailers)
        {
            Assert.Contains(SeedDataset.Credits, credit => credit.RetailerCode == retailer.Code);
        }
    }

    [Fact]
    public void Every_Retailer_Company_Pair_Has_A_Baseline_Credit_Line()
    {
        // billing_credit's baseline-coverage amendment (credits.data.ts's
        // header comment): 7 primary lines + 7 * 21 baseline lines = 154,
        // i.e. one credit line for every (retailer, company) pair.
        Assert.Equal(7 + (7 * 21), SeedDataset.Credits.Count);
        Assert.Equal(SeedDataset.Retailers.Count * SeedDataset.Companies.Count, SeedDataset.Credits.Count);

        foreach (var retailer in SeedDataset.Retailers)
        {
            foreach (var company in SeedDataset.Companies)
            {
                Assert.Contains(
                    SeedDataset.Credits,
                    credit => credit.RetailerCode == retailer.Code && credit.CompanyCode == company.Code);
            }
        }
    }

    [Fact]
    public void Five_Completed_Sagas_And_One_Cancelled_Saga_Are_Seeded()
    {
        Assert.Equal(6, SeedDataset.Sagas.Count);
        Assert.Equal(5, SeedDataset.CompletedSagas.Count);
        Assert.Single(SeedDataset.CancelledSagas);
        Assert.All(SeedDataset.CompletedSagas, saga => Assert.Equal("completed", saga.Status));
        Assert.All(SeedDataset.CancelledSagas, saga => Assert.Equal("cancelled", saga.Status));
    }

    [Fact]
    public void The_Cancelled_Saga_Total_Ends_In_Point_Ninety_Nine_Cents()
    {
        var cancelled = Assert.Single(SeedDataset.CancelledSagas);
        Assert.Equal(99, cancelled.TotalAmount % 100);
        Assert.Equal("credit_rejected", cancelled.CancellationReason);
    }

    /// <summary>
    /// Feature seed_job acceptance / #7's own documented invariant: "the
    /// GLN check digits must be genuinely valid — it fails loudly rather
    /// than write an invalid party identifier". Every retailer AND company
    /// GLN — the whole seeded catalogue, not a sample — is asserted to
    /// round-trip through <see cref="GLN"/>'s own constructor.
    /// </summary>
    [Fact]
    public void Every_Seeded_Gln_Is_Valid_Per_Gln()
    {
        foreach (var retailer in SeedDataset.Retailers)
        {
            var gln = new GLN(retailer.Gln);
            Assert.Equal(retailer.Gln, gln.Value);
        }

        foreach (var company in SeedDataset.Companies)
        {
            var gln = new GLN(company.Gln);
            Assert.Equal(company.Gln, gln.Value);
        }
    }

    [Fact]
    public void Every_Seeded_Gln_Is_Unique_Across_Retailers_And_Companies()
    {
        var glns = SeedDataset.Retailers.Select(r => r.Gln).Concat(SeedDataset.Companies.Select(c => c.Gln)).ToList();

        Assert.Equal(glns.Count, glns.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Ported from #7's own stock.data.ts invariant: every company the
    /// seeded sagas never touch gets a FULL row per product (so a live
    /// order against it can never hit a <c>stock.reserve</c> NOT_FOUND
    /// wall); a saga-touched company only carries rows for the specific
    /// products its own sagas reserved — deliberately NOT full coverage,
    /// exactly like #7.
    /// </summary>
    [Fact]
    public void Every_Non_Saga_Company_Has_Full_Stock_Coverage_Per_Product()
    {
        var sagaCompanyCodes = SeedDataset.Sagas.Select(saga => saga.CompanyCode).ToHashSet(StringComparer.Ordinal);
        var nonSagaCompanies = SeedDataset.Companies.Where(company => !sagaCompanyCodes.Contains(company.Code));

        foreach (var company in nonSagaCompanies)
        {
            foreach (var product in SeedDataset.Products)
            {
                Assert.Contains(
                    SeedDataset.Stock,
                    stock => stock.CompanyCode == company.Code && stock.ProductCode == product.Code);
            }
        }
    }

    [Fact]
    public void Every_Saga_Reserved_Company_Product_Pair_Has_A_Stock_Row()
    {
        foreach (var saga in SeedDataset.Sagas)
        {
            foreach (var reservation in saga.Reservations)
            {
                Assert.Contains(
                    SeedDataset.Stock,
                    stock => stock.CompanyCode == reservation.CompanyCode && stock.ProductCode == reservation.ProductCode);
            }
        }
    }

    [Fact]
    public void No_Stock_Row_Has_Negative_Units()
    {
        Assert.All(SeedDataset.Stock, stock => Assert.True(stock.Units >= 0));
    }
}
