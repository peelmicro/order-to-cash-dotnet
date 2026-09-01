using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// MS-SQL types per Databases doc §6 (credits, credit_items, invoices,
/// invoice_items, payments, invoice_number_sequences) and §4.3 (outbox,
/// processed_events, byte-identical to `otc_orders`/`otc_fulfillment`):
/// uniqueidentifier, datetime2(3), nvarchar, bigint IDENTITY for
/// outbox.seq, `int` (not `bigint`) for invoice_number_sequences.next_value.
/// Every assertion here reads <c>INFORMATION_SCHEMA.COLUMNS</c> from the
/// real database — never EF's own model metadata (feature db_orders
/// review: this is exactly what let seven of eight foreign keys and one
/// wrong column type go undetected the first time).
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
        // credits
        new("credits", "id", "uniqueidentifier"),
        new("credits", "code", "nvarchar", MaxLength: 30),
        new("credits", "retailer_code", "nvarchar", MaxLength: 20),
        new("credits", "company_code", "nvarchar", MaxLength: 20),
        new("credits", "credit_limit", "int"),
        new("credits", "currency_code", "char", MaxLength: 3),
        new("credits", "created_at", "datetime2", DatetimePrecision: 3),
        new("credits", "updated_at", "datetime2", DatetimePrecision: 3),

        // credit_items
        new("credit_items", "id", "uniqueidentifier"),
        new("credit_items", "credit_id", "uniqueidentifier"),
        new("credit_items", "order_reference", "nvarchar", MaxLength: 20),
        new("credit_items", "amount", "int"),
        new("credit_items", "type", "nvarchar", MaxLength: 20),
        new("credit_items", "credit_date", "datetime2", DatetimePrecision: 3),
        new("credit_items", "created_at", "datetime2", DatetimePrecision: 3),
        new("credit_items", "updated_at", "datetime2", DatetimePrecision: 3),

        // invoices
        new("invoices", "id", "uniqueidentifier"),
        new("invoices", "invoice_reference", "nvarchar", MaxLength: 20),
        new("invoices", "invoice_date", "datetime2", DatetimePrecision: 3),
        new("invoices", "company_code", "nvarchar", MaxLength: 20),
        new("invoices", "retailer_code", "nvarchar", MaxLength: 20),
        new("invoices", "order_reference", "nvarchar", MaxLength: 20),
        new("invoices", "amount", "int"),
        new("invoices", "discount", "int"),
        new("invoices", "total_amount", "int"),
        new("invoices", "currency_code", "char", MaxLength: 3),
        new("invoices", "status", "nvarchar", MaxLength: 20),
        new("invoices", "paid_at", "datetime2", DatetimePrecision: 3, Nullable: true),
        new("invoices", "created_at", "datetime2", DatetimePrecision: 3),
        new("invoices", "updated_at", "datetime2", DatetimePrecision: 3),

        // invoice_items
        new("invoice_items", "id", "uniqueidentifier"),
        new("invoice_items", "invoice_id", "uniqueidentifier"),
        new("invoice_items", "product_code", "nvarchar", MaxLength: 30),
        new("invoice_items", "units", "int"),
        new("invoice_items", "price", "int"),
        new("invoice_items", "created_at", "datetime2", DatetimePrecision: 3),
        new("invoice_items", "updated_at", "datetime2", DatetimePrecision: 3),

        // payments — append-only, no updated_at (Databases doc §3, §6)
        new("payments", "id", "uniqueidentifier"),
        new("payments", "payment_reference", "nvarchar", MaxLength: 30),
        new("payments", "invoice_id", "uniqueidentifier"),
        new("payments", "amount", "int"),
        new("payments", "currency_code", "char", MaxLength: 3),
        new("payments", "value_date", "datetime2", DatetimePrecision: 3),
        new("payments", "source", "nvarchar", MaxLength: 20),
        new("payments", "created_at", "datetime2", DatetimePrecision: 3),

        // invoice_number_sequences — `next_value` is `int`, NOT `bigint`
        // (task instructions, and feature db_orders review D2's lesson —
        // this is the third and last of the three sequence tables it named).
        new("invoice_number_sequences", "id", "int"),
        new("invoice_number_sequences", "next_value", "int"),

        // outbox — byte-identical to otc_orders/otc_fulfillment (§4.3)
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

        // processed_events — byte-identical to otc_orders/otc_fulfillment (§4.3)
        new("processed_events", "id", "uniqueidentifier"),
        new("processed_events", "event_id", "uniqueidentifier"),
        new("processed_events", "consumer", "nvarchar", MaxLength: 50),
        new("processed_events", "processed_at", "datetime2", DatetimePrecision: 3),
        new("processed_events", "created_at", "datetime2", DatetimePrecision: 3),
    ];

    [Fact]
    public async Task Every_Table_Has_The_Expected_Columns_And_SqlTypes()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_columns_{Guid.NewGuid():N}");
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
    /// Feature db_orders review D4's closure gap, closed from the start
    /// here: the whitelist test above proves every expected column exists
    /// with the right type, but never proves nothing *extra* exists — an
    /// accidental EF-inferred shadow column would pass it silently. Exactly
    /// 8 tables (excluding EF's own `__EFMigrationsHistory`), and per table,
    /// exactly the expected column set — no more, no fewer.
    /// </summary>
    [Fact]
    public async Task No_Unexpected_Table_Or_Column_Exists()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_closure_{Guid.NewGuid():N}");
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

        Assert.Equal(8, expectedColumnsByTable.Count);

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
