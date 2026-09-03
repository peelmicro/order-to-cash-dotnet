using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.2 — <c>FromSql</c> requires EVERY mapped column of the
/// entity type in the projection, and a missing one is a runtime error, not
/// a compile error. This compares <see cref="OutboxRelay.ClaimColumnNames"/>
/// against the <c>IEntityType</c>'s own mapped column names, built from a
/// real EF Core model with no database connection — so adding an
/// <c>outbox</c> column later cannot silently break the relay.
/// </summary>
public sealed class OutboxClaimProjectionTests
{
    [Fact]
    public void TheClaimStatementProjectsEveryMappedColumnOfTheOutboxEntity()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer("Server=unused;Database=unused;")
            .Options;
        using var db = new OrdersDbContext(options);

        var entityType = db.Model.FindEntityType(typeof(OutboxMessage));
        Assert.NotNull(entityType);

        var mappedColumnNames = entityType!.GetProperties()
            .Select(property => property.GetColumnName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var claimedColumnNames = OutboxRelay.ClaimColumnNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(mappedColumnNames, claimedColumnNames);
        Assert.Equal(mappedColumnNames.Length, OutboxRelay.ClaimColumnNames.Count);
    }
}
