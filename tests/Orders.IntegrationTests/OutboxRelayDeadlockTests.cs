using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;
using Xunit.Abstractions;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 87 — a CONSTRUCTED, deterministic deadlock (not the naturally
/// intermittent OI4 race, which 40 iterations against a real container,
/// cross-checked against <c>system_health</c>'s own <c>xml_deadlock_report</c>
/// ring buffer, could not reproduce even once — see
/// <c>DeadlockRetryExecutionStrategy</c>'s remarks for why: READPAST and the
/// stamp's own U-lock conversion priority make a row-lock cycle between two
/// ordinary relay claims impossible by construction).
/// </summary>
/// <remarks>
/// The cycle here is built from plain SQL Server lock-compatibility rules,
/// not a guess about physical page layout, so it forms every time:
/// <list type="number">
/// <item>The relay claims rows A and B (UPDLOCK, uncontested), then pauses
/// inside <c>PublishAsync</c> — OI13's own technique for holding a relay's
/// transaction open at a known point.</item>
/// <item>A second connection takes a <c>HOLDLOCK</c> (retained) S-lock on
/// row A — compatible with the relay's U-lock, so it succeeds immediately
/// even though the relay's transaction is still open.</item>
/// <item>That connection's SECOND statement asks for X on row B — blocked,
/// because the relay's claim holds U(B) and X is incompatible with U.</item>
/// <item>The relay is released: its stamp step needs to convert U(A) to X
/// — blocked, because the competitor holds S(A) and X is incompatible with
/// S.</item>
/// </list>
/// Relay waits for the competitor (wants A); the competitor waits for the
/// relay (wants B) — a genuine two-resource cycle. WHICH side SQL Server
/// kills is a deterministic property of the test, not a coin flip: the
/// competitor's own session raises its <c>SET DEADLOCK_PRIORITY</c> to
/// HIGH, and the relay's connection is left at the server default
/// (NORMAL) — HIGH always outranks NORMAL, so the relay is always the
/// victim, without this test needing to pin or otherwise manage the
/// relay's own connection (attempting that — via an explicit
/// <c>Database.OpenConnectionAsync()</c> held across the relay's own
/// automatic retries — produced a SEPARATE, genuine
/// <c>InvalidOperationException: The connection does not support Multiple
/// Active Result Sets</c> at DbContext disposal, which is exactly the kind
/// of "changing a guard's instrument swaps one set of premises for
/// another" hazard CLAUDE.md warns about: it is simpler, and no less
/// deterministic, to raise the COMPETITOR's priority than to lower the
/// relay's).
/// </remarks>
[Collection(MsSqlCollection.Name)]
public sealed class OutboxRelayDeadlockTests(MsSqlContainerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task OI15_RunOnceAsync_SurvivesAConstructedDeadlockVictimInsteadOfPropagatingTheSqlException()
    {
        void Log(string message) => output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi15_{Guid.NewGuid():N}");

        var rowA = NewRow();
        var rowB = NewRow();
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            seedDb.OutboxMessages.Add(rowA);
            await seedDb.SaveChangesAsync();
            seedDb.OutboxMessages.Add(rowB);
            await seedDb.SaveChangesAsync();
        }

        var releasePublish = new TaskCompletionSource();
        var publisher = new FakeFactPublisher
        {
            OnPublish = (_, _) => releasePublish.Task,
        };

        await using var relayDb = fixture.CreateDbContext(connectionString);

        var relay = BuildRelay(relayDb, publisher, batchSize: 2);
        Log("starting relay.RunOnceAsync");
        var relayTask = relay.RunOnceAsync(CancellationToken.None);

        // Bounded poll — relay has claimed both rows (UPDLOCK) and is now
        // paused inside PublishAsync, holding its transaction open.
        var claimDeadline = DateTime.UtcNow.AddSeconds(5);
        while (publisher.Calls.Count == 0 && DateTime.UtcNow < claimDeadline)
        {
            await Task.Delay(10);
        }
        Assert.NotEmpty(publisher.Calls);
        Log("relay has claimed and is paused in PublishAsync");

        await using var competitor = new SqlConnection(connectionString);
        await competitor.OpenAsync();

        await ExecuteAsync(competitor, "SET DEADLOCK_PRIORITY HIGH;");
        await ExecuteAsync(competitor, "BEGIN TRANSACTION;");
        await ExecuteScalarAsync(competitor, "SELECT id FROM dbo.outbox WITH (HOLDLOCK, ROWLOCK) WHERE id = @id;", rowA.Id);
        Log("competitor holds S(A)");

        // The competitor's second statement: X on row B, which the relay's
        // OWN claim already holds under U — this genuinely blocks, on a
        // background task, closing the cycle's second half.
        var competitorBlocked = Task.Run(async () =>
        {
            // SET payload = payload — a genuine write (takes X, closes the
            // cycle) that changes NOTHING: an earlier version of this test
            // wrote a literal, non-JSON string here, which the relay's own
            // (entirely correct) RETRY then re-claimed and tried to
            // publish, breaking on JSON parsing — a self-inflicted defect
            // in the harness, not in OutboxRelay.
            await ExecuteAsync(
                competitor,
                "UPDATE dbo.outbox WITH (ROWLOCK) SET payload = payload WHERE id = @id;",
                rowB.Id);
            Log("competitor's UPDATE on B completed (was blocked, now granted)");
        });

        // Give the competitor's second statement time to actually reach the
        // server and register as a genuine lock wait BEFORE resuming the
        // relay — otherwise the relay could stamp and commit before the
        // competitor even asks for row B, and no cycle would exist yet for
        // SQL Server to detect.
        await Task.Delay(500);
        Log("resuming relay's PublishAsync");

        // Resume the relay — its stamp step now needs X on row A, blocked
        // by the competitor's retained S-lock. The cycle is complete; SQL
        // Server's own deadlock monitor kills the LOW-priority session.
        releasePublish.SetResult();

        // Whichever task finishes first tells us the deadlock was resolved:
        // commit the competitor's transaction IMMEDIATELY once it does, so
        // any of the relay's own automatic retries see a clean table rather
        // than racing a still-open competitor transaction.
        var firstToFinish = await Task.WhenAny(relayTask, competitorBlocked, Task.Delay(TimeSpan.FromSeconds(45)));
        Log($"first to finish: {(firstToFinish == relayTask ? "relayTask" : firstToFinish == competitorBlocked ? "competitorBlocked" : "TIMEOUT")}");

        if (firstToFinish == competitorBlocked)
        {
            await competitorBlocked;
            await ExecuteAsync(competitor, "COMMIT TRANSACTION;");
            Log("competitor committed");
        }

        Exception? relayException = null;
        try
        {
            await relayTask;
            Log("relayTask completed WITHOUT throwing");
        }
        catch (Exception ex)
        {
            relayException = ex;
            Log($"relayTask THREW: {ex}");
        }

        if (firstToFinish != competitorBlocked)
        {
            await competitorBlocked;
            await ExecuteAsync(competitor, "COMMIT TRANSACTION;");
            Log("competitor committed (after relay)");
        }

        if (relayException is not null)
        {
            throw relayException;
        }

        // Whatever the retry raced away from, a following, uncontended poll
        // must reach full consistency — no row left behind, none published
        // twice.
        var followUpPublisher = new FakeFactPublisher();
        await using var followUpDb = fixture.CreateDbContext(connectionString);
        var followUpRelay = BuildRelay(followUpDb, followUpPublisher, batchSize: 2);
        await followUpRelay.RunOnceAsync(CancellationToken.None);
        Log("follow-up poll completed");

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(2, await assertDb.OutboxMessages.CountAsync(r => r.PublishedAt != null));
    }

    private static async Task ExecuteAsync(SqlConnection connection, string commandText, Guid? id = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        if (id is { } value)
        {
            command.Parameters.AddWithValue("@id", value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteScalarAsync(SqlConnection connection, string commandText, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteScalarAsync();
    }

    private static OutboxRelay BuildRelay(OrdersDbContext db, FakeFactPublisher publisher, int batchSize, int publishTimeoutMs = 20_000) =>
        new(db, publisher, new FakeClock(FakeClock.UtcNowToTheMillisecond()), Options.Create(new OutboxRelayOptions { BatchSize = batchSize, PublishTimeoutMs = publishTimeoutMs }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);

    private static OutboxMessage NewRow()
    {
        var now = DateTime.UtcNow;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventType = "order.placed.v1",
            AggregateId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            Payload = "{}",
            OccurredAt = now,
            CreatedAt = now,
        };
    }
}
