using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Backlog id 87 — a PURE test of <c>DeadlockRetryExecutionStrategy.ShouldRetryOn</c>
/// (invoked via reflection: it is <c>protected</c>, per EF Core's own
/// <c>ExecutionStrategy</c> base), built with a NO-connection <see cref="OrdersDbContext"/>
/// (design.md's own "unused" connection-string pattern, matching
/// <c>OutboxClaimProjectionTests</c>). Complements <c>OutboxRelayDeadlockTests</c>
/// (OI15), which only ever exercises the WRAPPED shape (the stamp's own
/// <c>ExecuteUpdateAsync</c>, which independently wraps a deadlock in
/// <see cref="InvalidOperationException"/> before this strategy ever sees
/// it) — CLAUDE.md's defeat-list row 9: "satisfy the closer half of a
/// two-part claim and leave the premise half stale". This proves the OTHER
/// half — a RAW, unwrapped <see cref="SqlException"/>, exactly what the
/// claim SELECT or the transaction's own Begin/Commit would raise — is
/// retried too, and that neither shape retries on a DIFFERENT SQL error
/// number or on an unrelated exception type.
/// </summary>
public sealed class DeadlockRetryExecutionStrategyTests
{
    private const int DeadlockVictimErrorNumber = 1205;
    private const int LockRequestTimeoutErrorNumber = 1222; // a real SQL Server error number, NOT a deadlock — the sibling substitution defeat-list row 3 asks for.

    [Fact]
    public void ShouldRetryOn_ARawDeadlockVictimSqlException_ReturnsTrue()
    {
        var sqlException = SqlExceptionFactory.WithNumber(DeadlockVictimErrorNumber, "Transaction (Process ID 1) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.");

        Assert.True(InvokeShouldRetryOn(sqlException));
    }

    [Fact]
    public void ShouldRetryOn_ADeadlockVictimSqlExceptionWrappedInInvalidOperationException_ReturnsTrue()
    {
        // The EXACT shape ExecuteUpdateAsync's own internal (non-retrying)
        // strategy produces — one level deeper than CallOnWrappedException
        // unwraps (it only peels DbUpdateException). OutboxRelayDeadlockTests
        // (OI15) proves this shape end to end against a real deadlock; this
        // proves the same shape in isolation, so the two together cover
        // both premises rather than one twice.
        var sqlException = SqlExceptionFactory.WithNumber(DeadlockVictimErrorNumber, "Transaction (Process ID 1) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.");
        var wrapped = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", sqlException);

        Assert.True(InvokeShouldRetryOn(wrapped));
    }

    [Fact]
    public void ShouldRetryOn_ARawSqlExceptionWithADifferentErrorNumber_ReturnsFalse()
    {
        var sqlException = SqlExceptionFactory.WithNumber(LockRequestTimeoutErrorNumber, "Lock request time out period exceeded.");

        Assert.False(InvokeShouldRetryOn(sqlException));
    }

    [Fact]
    public void ShouldRetryOn_ASqlExceptionWithADifferentErrorNumberWrappedInInvalidOperationException_ReturnsFalse()
    {
        var sqlException = SqlExceptionFactory.WithNumber(LockRequestTimeoutErrorNumber, "Lock request time out period exceeded.");
        var wrapped = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", sqlException);

        Assert.False(InvokeShouldRetryOn(wrapped));
    }

    [Fact]
    public void ShouldRetryOn_AnUnrelatedException_ReturnsFalse()
    {
        Assert.False(InvokeShouldRetryOn(new TimeoutException("unrelated")));
    }

    private static bool InvokeShouldRetryOn(Exception exception)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer("Server=unused;Database=unused;")
            .Options;
        using var db = new OrdersDbContext(options);
        var strategy = new DeadlockRetryExecutionStrategy(db);

        var method = typeof(DeadlockRetryExecutionStrategy).GetMethod(
            "ShouldRetryOn",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (bool)method.Invoke(strategy, [exception])!;
    }
}
