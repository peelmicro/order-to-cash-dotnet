using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature money_column_width, acceptance 4: "an architecture or schema
/// test asserts no money column is int". A positive list of the thirteen
/// known money columns would only prove those thirteen stayed widened — a
/// fourteenth money column, added later and left `int` by mistake, would
/// pass it silently. That is the exact failure this feature exists to
/// correct (`int` was carried through three phases on a mistaken "spec
/// parity" reading), so the check here is closed the other way round:
/// every `int`-typed column in the real schema is enumerated from
/// <c>INFORMATION_SCHEMA.COLUMNS</c>, and the test asserts that set is
/// *exactly* the small, named list of columns that are legitimately not
/// money (a unit count, a retry counter, a decimal-places count, a
/// sequence's own `id`/`next_value` — none of them minor-units amounts).
/// Any other `int` column — money or not — fails the test and forces a
/// deliberate decision: widen it, or name it here with a reason. A plain
/// whitelist of money-column names could not do this; only enumerating
/// *all* int columns and explaining every one of them closes the gap.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class NoMoneyColumnIsIntTests(MsSqlContainerFixture fixture)
{
    /// <summary>
    /// Every `int` column in `otc_orders` that is not a monetary amount,
    /// with the reason it is legitimately `int` rather than `bigint`.
    /// </summary>
    private static readonly HashSet<(string Table, string Column)> _knownNonMoneyIntColumns =
    [
        ("currencies", "decimal_points"), // count of decimal places (0-4), not an amount
        ("order_items", "quantity"), // a unit count, not a money value
        ("order_number_sequences", "id"), // sequence PK, per spec — not money
        ("order_number_sequences", "next_value"), // sequence counter, per spec — not money
        ("saga_commands", "attempts"), // retry counter, not money
    ];

    [Fact]
    public async Task No_Money_Column_Is_Int()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_no_int_money_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var actualIntColumns = new HashSet<(string Table, string Column)>();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_CATALOG = DB_NAME() AND DATA_TYPE = 'int' AND TABLE_NAME <> '__EFMigrationsHistory';
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actualIntColumns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var unexplainedIntColumns = actualIntColumns.Except(_knownNonMoneyIntColumns).ToList();

        Assert.True(
            unexplainedIntColumns.Count == 0,
            "Found int column(s) not accounted for as known non-money columns: " +
            string.Join(", ", unexplainedIntColumns.Select(c => $"{c.Table}.{c.Column}")) +
            ". If this is a monetary amount, widen it to bigint. If it is legitimately not money " +
            "(a count, a sequence value, a retry counter), add it to _knownNonMoneyIntColumns with a reason.");

        var missingKnownColumns = _knownNonMoneyIntColumns.Except(actualIntColumns).ToList();

        Assert.True(
            missingKnownColumns.Count == 0,
            "Known non-money int column(s) no longer exist in the schema (rename or type change?): " +
            string.Join(", ", missingKnownColumns.Select(c => $"{c.Table}.{c.Column}")) +
            ". Update _knownNonMoneyIntColumns to match.");
    }
}
