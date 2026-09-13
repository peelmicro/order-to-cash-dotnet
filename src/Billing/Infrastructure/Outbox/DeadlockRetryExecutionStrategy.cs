// COPY OF — src/Orders/Infrastructure/Outbox/DeadlockRetryExecutionStrategy.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OrderToCash.Billing.Infrastructure.Outbox;

/// <summary>
/// Backlog id 87 — the fix, scoped to <see cref="OutboxRelay.RunOnceAsync"/>
/// ONLY, never to the write model's own <c>DbContext</c> registration
/// (the service collection extension that registers it still calls plain
/// <c>UseSqlServer</c>, with no <c>EnableRetryOnFailure</c>).
/// </summary>
/// <remarks>
/// <para>
/// THE MECHANISM (established by decompiling the installed EF Core 10.0.11
/// assemblies, not by inference from the SQL Server hint text): when a
/// <see cref="DbContext"/> is configured with plain <c>UseSqlServer(...)</c>
/// (no retry enabled), <c>Database.CreateExecutionStrategy()</c> returns
/// <c>Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal.SqlServerExecutionStrategy</c>
/// — whose <c>RetriesOnFailure</c> is a hard-coded <see langword="false"/>,
/// and whose <c>ExecuteAsync</c> body is exactly:
/// <code>
/// try { return await operation(...); }
/// catch (Exception ex) when (CallOnWrappedException(ex, SqlServerTransientExceptionDetector.ShouldRetryOn))
/// {
///     throw new InvalidOperationException(SqlServerStrings.TransientExceptionDetected, ex);
/// }
/// </code>
/// It never retries anything — it only WRAPS a transient exception (one
/// <c>SqlServerTransientExceptionDetector.ShouldRetryOn</c> recognises,
/// which DOES include SQL error 1205 — decompiled and confirmed, the
/// deadlock-victim case sits in the detector's own switch) in the
/// <c>InvalidOperationException</c> whose message is the VERBATIM text this
/// backlog entry quotes, and rethrows. This is why <c>RunOnceAsync</c>
/// "escapes": the strategy already in force diagnoses the failure
/// correctly and then does nothing about it.
/// </para>
/// <para>
/// THE ORDERING QUESTION (backlog bullet 4): the claim's own
/// <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c> hint (<see cref="OutboxRelay"/>
/// line 98) makes a classic "two relays, opposite lock order" cycle
/// IMPOSSIBLE by construction, not merely unlikely — READPAST means a row
/// already locked by another session is skipped, never waited for, so the
/// claim SELECT can never become the blocked half of a cycle. And the
/// U-lock the claim takes is deliberately compatible with the transaction's
/// OWN later conversion to X in the stamp step, which SQL Server's lock
/// manager gives priority over any new incompatible request queued behind
/// it — the entire reason Update locks exist. Proved two ways: (1) by this
/// reasoning, and (2) empirically — 40 iterations of the real two-relay
/// race (OI4's own shape) against a fresh Testcontainers instance, then
/// cross-checked against <c>sys.dm_xe_session_targets</c>'s <c>system_health</c>
/// ring buffer, produced zero exceptions and zero recorded
/// <c>xml_deadlock_report</c> events. So the ordering is NOT the cause, and
/// reordering the claim would fix nothing.
/// </para>
/// <para>
/// The one statement in <c>RunOnceAsync</c>'s transaction that carries NO
/// lock hint at all is the stamp <c>ExecuteUpdateAsync</c> — every row it
/// touches, the SAME transaction's own claim already owns, so ordinarily
/// its U→X conversion is uncontested. <c>OutboxRelayDeadlockTests</c>
/// constructs the one case where it is NOT: a second connection holds a
/// HOLDLOCK-forced S-lock on a claimed row (compatible with U, so it is
/// granted while the claim is still open) and then itself requests X on the
/// OTHER claimed row (blocked by the claim's own U) — closing a genuine,
/// two-resource cycle around the stamp's conversion, which is real
/// row-lock incompatibility (S blocks X; X blocks U), not a guess about
/// physical page layout.
/// </para>
/// <para>
/// SAFE TO RETRY, UNLIKE <c>EfCoreUnitOfWork</c>: that class's own remarks
/// forbid enabling retries because a retried delegate could commit
/// aggregate rows whose already-drained domain events never reach the
/// outbox. <c>OutboxRelay</c>'s delegate has no such hazard — a retry after
/// this exact class of exception (the transaction rolled back, nothing
/// committed) is externally indistinguishable from the relay's own accepted
/// "died before stamping, the next poll re-claims and re-publishes" path
/// (OI5), which this codebase already treats as correct, at-least-once
/// delivery. Retrying immediately, in-process, changes nothing about that
/// contract — it only avoids surfacing the error as an unhandled exception
/// in the relay's own loop.
/// </para>
/// <para>
/// Deliberately narrower than <c>SqlServerRetryingExecutionStrategy</c>'s
/// full transient-error list (which would ALSO be safe here for the same
/// reason, but is not what this backlog entry asks for): retries ONLY
/// <c>SqlException</c> number 1205, the deadlock victim SQL Server's own
/// message names. Every other transient condition (a dropped connection, a
/// timeout) propagates exactly as it did before this fix.
/// </para>
/// </remarks>
public sealed class DeadlockRetryExecutionStrategy(DbContext context)
    : ExecutionStrategy(context, RetryCount, _retryDelay)
{
    private const int DeadlockVictimErrorNumber = 1205;
    private const int RetryCount = 3;
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(200);

    // ExecutionStrategy.ShouldRetryOn is declared `protected internal` in
    // Microsoft.EntityFrameworkCore.dll — a DIFFERENT assembly, so the
    // `internal` half is not ours to keep: `protected` is the correct,
    // and only legal, override accessibility from here (CS0507 otherwise).
    protected override bool ShouldRetryOn(Exception exception) =>
        ExtractSqlException(exception) is { } sqlException
        && sqlException.Errors.Cast<SqlError>().Any(error => error.Number == DeadlockVictimErrorNumber);

    /// <summary>
    /// Armed by <c>OutboxRelayDeadlockTests</c>: the stamp's own
    /// <c>ExecuteUpdateAsync</c> does NOT surface a raw <see cref="SqlException"/>
    /// to this outer strategy — it is a bulk "ExecuteUpdate" query, which
    /// wraps ITSELF with the DbContext's own CONFIGURED (still non-retrying)
    /// strategy independently of whichever outer strategy is calling it, so
    /// what reaches here is ALREADY the
    /// <c>SqlServerExecutionStrategy</c>-wrapped
    /// <see cref="InvalidOperationException"/>
    /// (<c>SqlServerStrings.TransientExceptionDetected</c>), one level
    /// deeper than <c>CallOnWrappedException</c> unwraps (which only peels
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>, per
    /// its own decompiled body — never this provider-specific wrapping).
    /// The claim SELECT and the transaction's own
    /// <c>BeginTransactionAsync</c>/<c>CommitAsync</c>, by contrast, are NOT
    /// wrapped a second time, so a raw <see cref="SqlException"/> reaching
    /// this method directly is equally possible and is checked first.
    /// </summary>
    private static SqlException? ExtractSqlException(Exception exception) => exception switch
    {
        SqlException sqlException => sqlException,
        { InnerException: SqlException innerSqlException } => innerSqlException,
        _ => null,
    };
}
