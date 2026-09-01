using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// MS-SQL types for `otc_notifications.processed_events` (Databases doc
/// §7, same shape as §4.3) — this is the ONLY table this context owns; the
/// Notifications service has no aggregate and no outbox (it consumes facts,
/// it does not produce them). Every assertion here reads
/// <c>INFORMATION_SCHEMA.COLUMNS</c> from the real database — never EF's
/// own model metadata.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class SchemaColumnTypeTests(MsSqlContainerFixture fixture)
{
    private sealed record ColumnRow(string DataType, int? CharacterMaximumLength, int? DatetimePrecision, bool IsNullable);

    private sealed record ExpectedColumn(
        string Table,
        string Column,
        string DataType,
        int? MaxLength = null,
        int? DatetimePrecision = null,
        bool Nullable = false);

    private static readonly ExpectedColumn[] _expected =
    [
        new("processed_events", "id", "uniqueidentifier"),
        new("processed_events", "event_id", "uniqueidentifier"),
        new("processed_events", "consumer", "nvarchar", MaxLength: 50),
        new("processed_events", "processed_at", "datetime2", DatetimePrecision: 3),
        new("processed_events", "created_at", "datetime2", DatetimePrecision: 3),
    ];

    [Fact]
    public async Task ProcessedEvents_Has_The_Expected_Columns_And_SqlTypes()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_columns_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var actual = new Dictionary<(string Table, string Column), ColumnRow>();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                       DATETIME_PRECISION, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_CATALOG = DB_NAME();
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var column = reader.GetString(1);
                var dataType = reader.GetString(2);
                int? maxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                int? datetimePrecision = reader.IsDBNull(4) ? null : (int)(short)reader.GetValue(4);
                var nullable = reader.GetString(5) == "YES";

                actual[(table, column)] = new ColumnRow(dataType, maxLength, datetimePrecision, nullable);
            }
        }

        var failures = new List<string>();

        foreach (var expected in _expected)
        {
            if (!actual.TryGetValue((expected.Table, expected.Column), out var row))
            {
                failures.Add($"{expected.Table}.{expected.Column}: column is missing");
                continue;
            }

            if (!string.Equals(row.DataType, expected.DataType, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{expected.Table}.{expected.Column}: expected data_type '{expected.DataType}', got '{row.DataType}'");
            }

            if (expected.MaxLength is not null && row.CharacterMaximumLength != expected.MaxLength)
            {
                failures.Add($"{expected.Table}.{expected.Column}: expected character_maximum_length {expected.MaxLength}, got {row.CharacterMaximumLength}");
            }

            if (expected.DatetimePrecision is not null && row.DatetimePrecision != expected.DatetimePrecision)
            {
                failures.Add($"{expected.Table}.{expected.Column}: expected datetime_precision {expected.DatetimePrecision}, got {row.DatetimePrecision}");
            }

            if (row.IsNullable != expected.Nullable)
            {
                failures.Add($"{expected.Table}.{expected.Column}: expected nullable={expected.Nullable}, got {row.IsNullable}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Feature db_billing, acceptance 3, restated at the column level:
    /// "otc_notifications contains processed_events and nothing else" —
    /// exactly one table, exactly the expected column set, no more, no
    /// fewer (feature db_orders review D4's closure lesson, applied from
    /// the start).
    /// </summary>
    [Fact]
    public async Task No_Unexpected_Table_Or_Column_Exists()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_closure_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var actualColumnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TABLE_NAME, COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME <> '__EFMigrationsHistory';
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var column = reader.GetString(1);

                if (!actualColumnsByTable.TryGetValue(table, out var columns))
                {
                    columns = new HashSet<string>(StringComparer.Ordinal);
                    actualColumnsByTable[table] = columns;
                }

                columns.Add(column);
            }
        }

        var expectedColumnsByTable = _expected
            .GroupBy(e => e.Table, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Column).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        Assert.Single(expectedColumnsByTable);

        var expectedTableNames = expectedColumnsByTable.Keys.OrderBy(t => t, StringComparer.Ordinal);
        var actualTableNames = actualColumnsByTable.Keys.OrderBy(t => t, StringComparer.Ordinal);
        Assert.Equal(expectedTableNames, actualTableNames);

        var failures = new List<string>();

        foreach (var (table, expectedColumns) in expectedColumnsByTable)
        {
            var actualColumns = actualColumnsByTable[table];

            var missing = expectedColumns.Except(actualColumns);
            var unexpected = actualColumns.Except(expectedColumns);

            if (missing.Any())
            {
                failures.Add($"{table}: missing columns [{string.Join(", ", missing)}]");
            }

            if (unexpected.Any())
            {
                failures.Add($"{table}: unexpected columns [{string.Join(", ", unexpected)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
