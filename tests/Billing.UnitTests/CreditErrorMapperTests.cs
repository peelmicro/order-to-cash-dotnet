using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BC27`, ledger `L19` — every TRANSIENT store failure this service can
/// map produces a code the saga adapter treats as retryable, and
/// `CONFLICT` is produced by NOTHING. The terminal set is read from
/// <c>NatsSagaCommandsAdapter</c>'s OWN classification (as TEXT — the two
/// services share no assembly reference) rather than retyped, the
/// <c>StockRpcErrorMapperTests</c> instrument.
/// </summary>
public sealed partial class CreditErrorMapperTests
{
    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void BC27_MapsEveryTransientStoreFailureToACodeTheSagaAdapterTreatsAsRetryable_NeverToATerminalBusinessCode(Exception exception)
    {
        var terminalSet = ReadTerminalCodeSet();

        var reply = CreditErrorMapper.Map(exception, DateTimeOffset.UtcNow);

        Assert.DoesNotContain(reply.Code, terminalSet);
    }

    [Fact]
    public void ConflictIsProducedByNoInputAtAll()
    {
        var candidates = new Exception[]
        {
            new InvalidCreditRequestError("bad request"),
            new CreditLineNotFoundError("CarrefourEs", "IBERFOODS"),
            new CreditCurrencyMismatchError("EUR", "GBP"),
            new CreditLimitExceededError(1_000, 500),
            new CreditRefusalMismatchError(1_000, 2_000),
            new CreditReleaseUnderflowError("ORD-000001", 500, 600),
            new NoActiveHoldError("ORD-000001"),
            new CreditLedgerOverflowError(new OverflowException()),
            BuildDeadlockException(),
            new DbUpdateConcurrencyException("optimistic concurrency conflict"),
            new InvalidOperationException("anything else"),
        };

        Assert.All(candidates, exception => Assert.NotEqual("CONFLICT", CreditErrorMapper.Map(exception, DateTimeOffset.UtcNow).Code));
    }

    [Fact]
    public void CreditLedgerOverflowError_MapsToDomainErrorTerminal_NeverRetryable()
    {
        var reply = CreditErrorMapper.Map(new CreditLedgerOverflowError(new OverflowException()), DateTimeOffset.UtcNow);

        Assert.Equal("DOMAIN_ERROR", reply.Code);
        var terminalSet = ReadTerminalCodeSet();
        Assert.Contains(reply.Code, terminalSet);
    }

    public static TheoryData<Exception> TransientExceptions() => new()
    {
        SqlExceptionFactory.WithNumber(1205, "deadlock victim"),
        SqlExceptionFactory.WithNumber(1222, "lock request timeout period exceeded"),
        SqlExceptionFactory.WithNumber(4060, "cannot open database (any other SqlException number)"),
        new DbUpdateConcurrencyException("optimistic concurrency conflict"),
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
