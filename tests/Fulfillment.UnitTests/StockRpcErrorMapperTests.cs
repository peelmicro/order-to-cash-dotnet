using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// `FS21`, ledger L7 — every TRANSIENT store failure this service can map
/// produces a code the saga adapter treats as retryable, and `CONFLICT` is
/// produced by NOTHING. The terminal set is read from
/// <c>NatsSagaCommandsAdapter</c>'s OWN classification (as TEXT — the two
/// services share no assembly reference) rather than retyped, so a future
/// change on the Orders side breaks THIS test rather than silently changing
/// this service's meaning.
/// </summary>
public sealed partial class StockRpcErrorMapperTests
{
    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void FS21_MapsEveryTransientStoreFailureToACodeTheSagaAdapterTreatsAsRetryable_NeverToATerminalBusinessCode(Exception exception)
    {
        var terminalSet = ReadTerminalCodeSet();

        var reply = StockErrorMapper.Map(exception, DateTimeOffset.UtcNow);

        Assert.DoesNotContain(reply.Code, terminalSet);
    }

    [Fact]
    public void ConflictIsProducedByNoInputAtAll()
    {
        var candidates = new Exception[]
        {
            new InvalidStockRequestError("bad request"),
            new NoKnownStockItemError("ACME"),
            new UnknownStockItemError("ACME", "P1"),
            new ReservationTerminalError(Fulfillment.Domain.ReservationStatus.Consumed, "release"),
            new ConcurrentReservationChangeError("ORD-000001"),
            BuildDeadlockException(),
            new DbUpdateConcurrencyException("optimistic concurrency conflict"),
            new InvalidOperationException("anything else"),
        };

        Assert.All(candidates, exception => Assert.NotEqual("CONFLICT", StockErrorMapper.Map(exception, DateTimeOffset.UtcNow).Code));
    }

    public static TheoryData<Exception> TransientExceptions() => new()
    {
        SqlExceptionFactory.WithNumber(1205, "deadlock victim"),
        SqlExceptionFactory.WithNumber(1222, "lock request timeout period exceeded"),
        SqlExceptionFactory.WithNumber(4060, "cannot open database (any other SqlException number)"),
        new DbUpdateConcurrencyException("optimistic concurrency conflict"),
        new ConcurrentReservationChangeError("ORD-000001"),
        new InvalidOperationException("something unexpected"),
    };

    private static Exception BuildDeadlockException() => SqlExceptionFactory.WithNumber(1205, "deadlock victim");

    private static HashSet<string> ReadTerminalCodeSet()
    {
        var adapterPath = RepositoryPaths.Find(Path.Combine("src", "Orders", "Infrastructure", "Messaging", "NatsSagaCommandsAdapter.cs"));
        var source = File.ReadAllText(adapterPath);

        var methodMatch = IsTerminalMethodRegex().Match(source);
        Assert.True(methodMatch.Success, "could not locate IsTerminalRpcErrorCode's switch body in NatsSagaCommandsAdapter.cs");

        var codes = CodeLiteralRegex().Matches(methodMatch.Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(codes);

        return codes;
    }

    [GeneratedRegex(@"IsTerminalRpcErrorCode\(string code\) => code switch\s*\{(.*?)\};", RegexOptions.Singleline)]
    private static partial Regex IsTerminalMethodRegex();

    [GeneratedRegex("\"([A-Z_]+)\"")]
    private static partial Regex CodeLiteralRegex();
}
