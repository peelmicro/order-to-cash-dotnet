using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FulfillmentContext = OrderToCash.Fulfillment.Infrastructure.Persistence.FulfillmentDbContext;
using NotificationsContext = OrderToCash.Notifications.Infrastructure.Persistence.NotificationsDbContext;
using OrdersContext = OrderToCash.Orders.Infrastructure.Persistence.OrdersDbContext;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Feature db_billing, acceptance 2: "outbox/processed_events parity test
/// across all four DbContexts passes" — this feature's distinctive
/// deliverable, and the .NET equivalent of #7's `apps/seed/outbox-parity.spec.ts`
/// (Databases doc §3: "Reliability tables (outbox, processed_events) have
/// byte-identical definitions in otc_orders, otc_fulfillment and
/// otc_billing"). Asserted from the DATABASE, never from EF metadata: each
/// of the four contexts migrates a fresh real MS-SQL database on the shared
/// container, then <c>INFORMATION_SCHEMA.COLUMNS</c> and the
/// <c>sys.indexes</c> catalogue are read directly and compared. `Orders` is
/// the reference (it is the oldest, feature db_orders); every other
/// context is compared against it, and a divergence names BOTH sides so the
/// failure is actionable, not just "Assert.Equal() Failure".
///
/// This project references `Orders.csproj`, `Fulfillment.csproj` and
/// `Notifications.csproj` ONLY for this file's cross-context comparison
/// (this feature's task constraints: reference those projects, never edit
/// them).
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class ReliabilityTableParityTests(MsSqlContainerFixture fixture)
{
    private sealed record ColumnShape(string Name, int Ordinal, string DataType, int? MaxLength, int? DatetimePrecision, bool Nullable);

    private sealed record IndexShape(string Name, bool Unique, string[] Columns);

    [Fact]
    public async Task Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts()
    {
        var ordersConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_parity_{Guid.NewGuid():N}");
        var fulfillmentConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_parity_{Guid.NewGuid():N}");
        var billingConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_parity_{Guid.NewGuid():N}");
        var notificationsConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_parity_{Guid.NewGuid():N}");

        await using (var db = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseSqlServer(ordersConnectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = new FulfillmentContext(new DbContextOptionsBuilder<FulfillmentContext>().UseSqlServer(fulfillmentConnectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = fixture.CreateDbContext(billingConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await using (var db = new NotificationsContext(new DbContextOptionsBuilder<NotificationsContext>().UseSqlServer(notificationsConnectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        // otc_notifications has no outbox — the Notifications service
        // produces no facts, only consumes them (Databases doc §7). Included
        // explicitly in the "otbox" half of the comparison by its absence,
        // per the task's instruction to say so rather than silently exclude
        // it.
        var outboxColumnsByContext = new Dictionary<string, List<ColumnShape>>(StringComparer.Ordinal)
        {
            ["Orders"] = await GetColumnsAsync(ordersConnectionString, "outbox"),
            ["Fulfillment"] = await GetColumnsAsync(fulfillmentConnectionString, "outbox"),
            ["Billing"] = await GetColumnsAsync(billingConnectionString, "outbox"),
        };
        var outboxIndexesByContext = new Dictionary<string, List<IndexShape>>(StringComparer.Ordinal)
        {
            ["Orders"] = await GetIndexesAsync(ordersConnectionString, "outbox"),
            ["Fulfillment"] = await GetIndexesAsync(fulfillmentConnectionString, "outbox"),
            ["Billing"] = await GetIndexesAsync(billingConnectionString, "outbox"),
        };

        var processedEventsColumnsByContext = new Dictionary<string, List<ColumnShape>>(StringComparer.Ordinal)
        {
            ["Orders"] = await GetColumnsAsync(ordersConnectionString, "processed_events"),
            ["Fulfillment"] = await GetColumnsAsync(fulfillmentConnectionString, "processed_events"),
            ["Billing"] = await GetColumnsAsync(billingConnectionString, "processed_events"),
            ["Notifications"] = await GetColumnsAsync(notificationsConnectionString, "processed_events"),
        };
        var processedEventsIndexesByContext = new Dictionary<string, List<IndexShape>>(StringComparer.Ordinal)
        {
            ["Orders"] = await GetIndexesAsync(ordersConnectionString, "processed_events"),
            ["Fulfillment"] = await GetIndexesAsync(fulfillmentConnectionString, "processed_events"),
            ["Billing"] = await GetIndexesAsync(billingConnectionString, "processed_events"),
            ["Notifications"] = await GetIndexesAsync(notificationsConnectionString, "processed_events"),
        };

        var failures = new List<string>();

        const string reference = "Orders";

        foreach (var (context, columns) in outboxColumnsByContext)
        {
            if (context == reference)
            {
                continue;
            }

            failures.AddRange(CompareColumns("outbox", reference, outboxColumnsByContext[reference], context, columns));
        }

        foreach (var (context, indexes) in outboxIndexesByContext)
        {
            if (context == reference)
            {
                continue;
            }

            failures.AddRange(CompareIndexes("outbox", reference, outboxIndexesByContext[reference], context, indexes));
        }

        foreach (var (context, columns) in processedEventsColumnsByContext)
        {
            if (context == reference)
            {
                continue;
            }

            failures.AddRange(CompareColumns("processed_events", reference, processedEventsColumnsByContext[reference], context, columns));
        }

        foreach (var (context, indexes) in processedEventsIndexesByContext)
        {
            if (context == reference)
            {
                continue;
            }

            failures.AddRange(CompareIndexes("processed_events", reference, processedEventsIndexesByContext[reference], context, indexes));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Feature db_billing, acceptance 3: "otc_notifications contains
    /// processed_events and nothing else" — read from
    /// <c>INFORMATION_SCHEMA.TABLES</c> on the real database, a closed-set
    /// assertion (feature db_orders review D4's lesson), not just "the
    /// expected table exists".
    /// </summary>
    [Fact]
    public async Task Notifications_Database_Contains_Only_ProcessedEvents()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_closure_{Guid.NewGuid():N}");

        await using (var db = new NotificationsContext(new DbContextOptionsBuilder<NotificationsContext>().UseSqlServer(connectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        var tables = new List<string>();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME <> '__EFMigrationsHistory';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["processed_events"], tables);
    }

    private static async Task<List<ColumnShape>> GetColumnsAsync(string connectionString, string table)
    {
        var columns = new List<ColumnShape>();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COLUMN_NAME, ORDINAL_POSITION, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                   DATETIME_PRECISION, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_CATALOG = DB_NAME() AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var ordinal = reader.GetInt32(1);
            var dataType = reader.GetString(2);
            int? maxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            int? datetimePrecision = reader.IsDBNull(4) ? null : (int)(short)reader.GetValue(4);
            var nullable = reader.GetString(5) == "YES";

            columns.Add(new ColumnShape(name, ordinal, dataType, maxLength, datetimePrecision, nullable));
        }

        return columns;
    }

    private static async Task<List<IndexShape>> GetIndexesAsync(string connectionString, string table)
    {
        var grouped = new Dictionary<string, (bool Unique, List<string> Columns)>(StringComparer.Ordinal);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name AS index_name, i.is_unique, c.name AS column_name, ic.key_ordinal
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 0 AND i.name IS NOT NULL AND t.name = @table
            ORDER BY i.name, ic.key_ordinal;
            """;
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var unique = reader.GetBoolean(1);
            var column = reader.GetString(2);

            if (!grouped.TryGetValue(name, out var entry))
            {
                entry = (unique, []);
                grouped[name] = entry;
            }

            entry.Columns.Add(column);
            grouped[name] = entry;
        }

        return [.. grouped.Select(kv => new IndexShape(kv.Key, kv.Value.Unique, [.. kv.Value.Columns]))];
    }

    private static List<string> CompareColumns(string table, string contextA, List<ColumnShape> a, string contextB, List<ColumnShape> b)
    {
        var failures = new List<string>();

        var namesA = a.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var namesB = b.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var missing in namesA.Except(namesB))
        {
            failures.Add($"{table}: column '{missing}' present in {contextA} but missing in {contextB}");
        }

        foreach (var extra in namesB.Except(namesA))
        {
            failures.Add($"{table}: column '{extra}' present in {contextB} but missing in {contextA}");
        }

        foreach (var columnA in a)
        {
            var columnB = b.FirstOrDefault(c => c.Name == columnA.Name);
            if (columnB is null)
            {
                continue; // already reported above
            }

            if (!string.Equals(columnA.DataType, columnB.DataType, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{table}.{columnA.Name}: data_type {contextA}='{columnA.DataType}' vs {contextB}='{columnB.DataType}'");
            }

            if (columnA.MaxLength != columnB.MaxLength)
            {
                failures.Add($"{table}.{columnA.Name}: character_maximum_length {contextA}={columnA.MaxLength} vs {contextB}={columnB.MaxLength}");
            }

            if (columnA.DatetimePrecision != columnB.DatetimePrecision)
            {
                failures.Add($"{table}.{columnA.Name}: datetime_precision {contextA}={columnA.DatetimePrecision} vs {contextB}={columnB.DatetimePrecision}");
            }

            if (columnA.Nullable != columnB.Nullable)
            {
                failures.Add($"{table}.{columnA.Name}: nullable {contextA}={columnA.Nullable} vs {contextB}={columnB.Nullable}");
            }

            if (columnA.Ordinal != columnB.Ordinal)
            {
                failures.Add($"{table}.{columnA.Name}: ordinal_position {contextA}={columnA.Ordinal} vs {contextB}={columnB.Ordinal}");
            }
        }

        return failures;
    }

    private static List<string> CompareIndexes(string table, string contextA, List<IndexShape> a, string contextB, List<IndexShape> b)
    {
        var failures = new List<string>();

        var namesA = a.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var namesB = b.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var missing in namesA.Except(namesB))
        {
            failures.Add($"{table}: index '{missing}' present in {contextA} but missing in {contextB}");
        }

        foreach (var extra in namesB.Except(namesA))
        {
            failures.Add($"{table}: index '{extra}' present in {contextB} but missing in {contextA}");
        }

        foreach (var indexA in a)
        {
            var indexB = b.FirstOrDefault(i => i.Name == indexA.Name);
            if (indexB is null)
            {
                continue; // already reported above
            }

            if (indexA.Unique != indexB.Unique)
            {
                failures.Add($"{table} index '{indexA.Name}': unique {contextA}={indexA.Unique} vs {contextB}={indexB.Unique}");
            }

            if (!indexA.Columns.SequenceEqual(indexB.Columns))
            {
                failures.Add(
                    $"{table} index '{indexA.Name}': columns {contextA}=[{string.Join(",", indexA.Columns)}] " +
                    $"vs {contextB}=[{string.Join(",", indexB.Columns)}]");
            }
        }

        return failures;
    }
}
