using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// The <c>OutboxClaimProjectionTests</c>/<c>StockClaimProjectionTests</c>
/// instrument, applied to the two locking statements of
/// <see cref="EfCoreBuyerCreditRepository"/> — <c>FromSqlInterpolated</c>
/// requires EVERY mapped column of the entity type in the projection, and a
/// missing one is a runtime error, not a compile error. Built from a real
/// EF Core model with no database connection.
/// </summary>
public sealed class CreditClaimProjectionTests
{
    [Fact]
    public void TheCreditLockStatementProjectsEveryMappedColumnOfTheCreditEntity()
    {
        using var db = BuildDbContext();

        var entityType = db.Model.FindEntityType(typeof(Credit));
        Assert.NotNull(entityType);

        AssertSameColumns(entityType!, EfCoreBuyerCreditRepository.CreditClaimColumnNames);
    }

    [Fact]
    public void TheCreditItemsLockStatementProjectsEveryMappedColumnOfTheCreditItemEntity()
    {
        using var db = BuildDbContext();

        var entityType = db.Model.FindEntityType(typeof(CreditItem));
        Assert.NotNull(entityType);

        AssertSameColumns(entityType!, EfCoreBuyerCreditRepository.CreditItemClaimColumnNames);
    }

    private static BillingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlServer("Server=unused;Database=unused;")
            .Options;
        return new BillingDbContext(options);
    }

    private static void AssertSameColumns(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType, IReadOnlyList<string> claimedColumnNames)
    {
        var mappedColumnNames = entityType.GetProperties()
            .Select(property => property.GetColumnName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var ordered = claimedColumnNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(mappedColumnNames, ordered);
        Assert.Equal(mappedColumnNames.Length, claimedColumnNames.Count);
    }
}
