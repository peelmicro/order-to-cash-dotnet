using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature db_orders, acceptance 2: "MS-SQL types per the spec:
/// uniqueidentifier, datetime2(3), nvarchar, bigint IDENTITY for
/// outbox.seq". Every assertion here reads
/// <c>INFORMATION_SCHEMA.COLUMNS</c> from the real database — never EF's own
/// model metadata, which would only prove EF agrees with itself.
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
        // currencies
        new("currencies", "id", "uniqueidentifier"),
        new("currencies", "code", "nvarchar", MaxLength: 3),
        new("currencies", "iso_number", "nvarchar", MaxLength: 3),
        new("currencies", "symbol", "nvarchar", MaxLength: 5),
        new("currencies", "decimal_points", "int"),
        new("currencies", "created_at", "datetime2", DatetimePrecision: 3),
        new("currencies", "updated_at", "datetime2", DatetimePrecision: 3),

        // products
        new("products", "id", "uniqueidentifier"),
        new("products", "code", "nvarchar", MaxLength: 30),
        new("products", "ean", "nvarchar", MaxLength: 13),
        new("products", "name", "nvarchar", MaxLength: 100),
        new("products", "description", "nvarchar", MaxLength: 255),
        new("products", "price", "bigint"),
        new("products", "currency_id", "uniqueidentifier"),
        new("products", "disabled_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("products", "created_at", "datetime2", DatetimePrecision: 3),
        new("products", "updated_at", "datetime2", DatetimePrecision: 3),

        // retailers
        new("retailers", "id", "uniqueidentifier"),
        new("retailers", "code", "nvarchar", MaxLength: 20),
        new("retailers", "name", "nvarchar", MaxLength: 100),
        new("retailers", "country", "nvarchar", MaxLength: 2),
        new("retailers", "vat", "nvarchar", MaxLength: 15),
        new("retailers", "gln", "nvarchar", MaxLength: 13),
        new("retailers", "currency_id", "uniqueidentifier"),
        new("retailers", "disabled_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("retailers", "created_at", "datetime2", DatetimePrecision: 3),
        new("retailers", "updated_at", "datetime2", DatetimePrecision: 3),

        // companies
        new("companies", "id", "uniqueidentifier"),
        new("companies", "code", "nvarchar", MaxLength: 20),
        new("companies", "name", "nvarchar", MaxLength: 100),
        new("companies", "country", "nvarchar", MaxLength: 2),
        new("companies", "vat", "nvarchar", MaxLength: 15),
        new("companies", "gln", "nvarchar", MaxLength: 13),
        new("companies", "currency_id", "uniqueidentifier"),
        new("companies", "disabled_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("companies", "created_at", "datetime2", DatetimePrecision: 3),
        new("companies", "updated_at", "datetime2", DatetimePrecision: 3),

        // orders
        new("orders", "id", "uniqueidentifier"),
        new("orders", "order_reference", "nvarchar", MaxLength: 20),
        new("orders", "order_date", "datetime2", DatetimePrecision: 3),
        new("orders", "company_id", "uniqueidentifier"),
        new("orders", "retailer_id", "uniqueidentifier"),
        new("orders", "currency_id", "uniqueidentifier"),
        new("orders", "initial_amount", "bigint"),
        new("orders", "initial_discount", "bigint"),
        new("orders", "total_amount", "bigint"),
        new("orders", "status", "nvarchar", MaxLength: 20),
        new("orders", "cancellation_reason", "nvarchar", MaxLength: 100, Nullable: true),
        new("orders", "notes", "nvarchar", MaxLength: -1, Nullable: true),
        new("orders", "created_at", "datetime2", DatetimePrecision: 3),
        new("orders", "updated_at", "datetime2", DatetimePrecision: 3),

        // order_items
        new("order_items", "id", "uniqueidentifier"),
        new("order_items", "order_id", "uniqueidentifier"),
        new("order_items", "product_id", "uniqueidentifier"),
        new("order_items", "description", "nvarchar", MaxLength: 255),
        new("order_items", "price", "bigint"),
        new("order_items", "quantity", "int"),
        new("order_items", "discount", "bigint"),
        new("order_items", "created_at", "datetime2", DatetimePrecision: 3),
        new("order_items", "updated_at", "datetime2", DatetimePrecision: 3),

        // order_number_sequences
        new("order_number_sequences", "id", "int"),
        new("order_number_sequences", "next_value", "int"),

        // outbox
        new("outbox", "id", "uniqueidentifier"),
        new("outbox", "event_id", "uniqueidentifier"),
        new("outbox", "event_type", "nvarchar", MaxLength: 60),
        new("outbox", "aggregate_id", "uniqueidentifier"),
        new("outbox", "correlation_id", "uniqueidentifier"),
        new("outbox", "causation_id", "uniqueidentifier"),
        new("outbox", "payload", "nvarchar", MaxLength: -1),
        new("outbox", "occurred_at", "datetime2", DatetimePrecision: 3),
        new("outbox", "published_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("outbox", "created_at", "datetime2", DatetimePrecision: 3),
        new("outbox", "seq", "bigint"),
        new("outbox", "trace_parent", "nvarchar", MaxLength: 64, Nullable: true),

        // processed_events
        new("processed_events", "id", "uniqueidentifier"),
        new("processed_events", "event_id", "uniqueidentifier"),
        new("processed_events", "consumer", "nvarchar", MaxLength: 50),
        new("processed_events", "processed_at", "datetime2", DatetimePrecision: 3),
        new("processed_events", "created_at", "datetime2", DatetimePrecision: 3),

        // saga_commands
        new("saga_commands", "id", "uniqueidentifier"),
        new("saga_commands", "order_id", "uniqueidentifier"),
        new("saga_commands", "order_reference", "nvarchar", MaxLength: 20),
        new("saga_commands", "command", "nvarchar", MaxLength: 30),
        new("saga_commands", "payload", "nvarchar", MaxLength: -1),
        new("saga_commands", "triggering_event_id", "uniqueidentifier"),
        new("saga_commands", "status", "nvarchar", MaxLength: 10),
        new("saga_commands", "attempts", "int"),
        new("saga_commands", "last_error", "nvarchar", MaxLength: -1, Nullable: true),
        new("saga_commands", "next_attempt_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("saga_commands", "created_at", "datetime2", DatetimePrecision: 3),
        new("saga_commands", "updated_at", "datetime2", DatetimePrecision: 3),
        new("saga_commands", "sent_at", "datetime2", DatetimePrecision: 3, Nullable: true),

        // saga_ignored_facts
        new("saga_ignored_facts", "id", "uniqueidentifier"),
        new("saga_ignored_facts", "event_id", "uniqueidentifier"),
        new("saga_ignored_facts", "event_type", "nvarchar", MaxLength: 60),
        new("saga_ignored_facts", "order_id", "uniqueidentifier", Nullable: true),
        new("saga_ignored_facts", "correlation_id", "uniqueidentifier"),
        new("saga_ignored_facts", "observed_status", "nvarchar", MaxLength: 20, Nullable: true),
        new("saga_ignored_facts", "expected_status", "nvarchar", MaxLength: 20, Nullable: true),
        new("saga_ignored_facts", "marker", "nvarchar", MaxLength: 20),
        new("saga_ignored_facts", "recorded_at", "datetime2", DatetimePrecision: 3),
    ];

    [Fact]
    public async Task Every_Table_Has_The_Expected_Columns_And_SqlTypes()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_columns_{Guid.NewGuid():N}");
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
    /// Review D4: the whitelist test above proves every expected column
    /// exists with the right type, but never proves nothing *extra* exists
    /// — an accidental EF-inferred shadow column (e.g. a stray FK shadow
    /// property from a misconfigured navigation) would pass it silently.
    /// This closes that gap: exactly 11 tables (excluding EF's own
    /// `__EFMigrationsHistory`), and per table, exactly the expected column
    /// set — no more, no fewer.
    /// </summary>
    [Fact]
    public async Task No_Unexpected_Table_Or_Column_Exists()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_closure_{Guid.NewGuid():N}");
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

        Assert.Equal(11, expectedColumnsByTable.Count);

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
