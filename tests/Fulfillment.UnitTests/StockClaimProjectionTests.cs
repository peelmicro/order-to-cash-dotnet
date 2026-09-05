using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The <c>OutboxClaimProjectionTests</c> instrument, applied to the two
/// locking statements of <see cref="EfCoreStockItemRepository"/> —
/// <c>FromSqlInterpolated</c> requires EVERY mapped column of the entity
/// type in the projection, and a missing one is a runtime error, not a
/// compile error. Built from a real EF Core model with no database
/// connection.
/// </summary>
public sealed class StockClaimProjectionTests
{
    [Fact]
    public void TheStockLockStatementProjectsEveryMappedColumnOfTheStockEntity()
    {
        using var db = BuildDbContext();

        var entityType = db.Model.FindEntityType(typeof(Stock));
        Assert.NotNull(entityType);

        AssertSameColumns(entityType!, EfCoreStockItemRepository.StockClaimColumnNames);
    }

    [Fact]
    public void TheReservationsLockStatementProjectsEveryMappedColumnOfTheReservationEntity()
    {
        using var db = BuildDbContext();

        var entityType = db.Model.FindEntityType(typeof(Reservation));
        Assert.NotNull(entityType);

        AssertSameColumns(entityType!, EfCoreStockItemRepository.ReservationClaimColumnNames);
    }

    private static FulfillmentDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseSqlServer("Server=unused;Database=unused;")
            .Options;
        return new FulfillmentDbContext(options);
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
