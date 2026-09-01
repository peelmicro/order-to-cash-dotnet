using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Every index named in Databases doc §6 (`credits` unique `code` and
/// unique `(retailer_code, company_code)`; `credit_items`
/// `(credit_id, order_reference)`; `invoices` unique `invoice_reference`,
/// unique `order_reference` and `(status, invoice_date)`; `payments` unique
/// `payment_reference` — the remittance endpoint's idempotency key,
/// R47/R48) plus the reliability indexes carried over from features
/// db_orders/db_fulfillment (§4.3, byte-identical), read from
/// <c>sys.indexes</c> / <c>sys.index_columns</c> on the real database, not
/// from EF's model. Column order inside a composite index is asserted via
/// <c>SequenceEqual</c>.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class IndexTests(MsSqlContainerFixture fixture)
{
    private sealed record ExpectedIndex(string Table, string[] Columns, bool Unique);

    private static readonly ExpectedIndex[] _expected =
    [
        new("credits", ["code"], true),
        new("credits", ["retailer_code", "company_code"], true),

        new("credit_items", ["credit_id", "order_reference"], false),

        new("invoices", ["invoice_reference"], true),
        new("invoices", ["order_reference"], true),
        new("invoices", ["status", "invoice_date"], false),

        new("payments", ["payment_reference"], true),

        new("outbox", ["event_id"], true),
        new("outbox", ["seq"], true),
        new("outbox", ["published_at", "seq"], false),
        new("outbox", ["published_at", "occurred_at"], false),

        new("processed_events", ["event_id", "consumer"], true),
    ];

    private sealed record ActualIndex(string Table, string Name, bool Unique, string[] Columns);

    [Fact]
    public async Task Every_Spec_Index_Exists_With_The_Expected_Columns_And_Uniqueness()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_indexes_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var actual = new List<ActualIndex>();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT t.name AS table_name, i.name AS index_name, i.is_unique,
                       c.name AS column_name, ic.key_ordinal
                FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.is_primary_key = 0 AND i.name IS NOT NULL
                ORDER BY t.name, i.name, ic.key_ordinal;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            var grouped = new Dictionary<(string Table, string Name), (bool Unique, List<string> Columns)>();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                var name = reader.GetString(1);
                var unique = reader.GetBoolean(2);
                var column = reader.GetString(3);

                var key = (table, name);
                if (!grouped.TryGetValue(key, out var entry))
                {
                    entry = (unique, []);
                    grouped[key] = entry;
                }

                entry.Columns.Add(column);
                grouped[key] = entry;
            }

            foreach (var ((table, name), (unique, columns)) in grouped)
            {
                actual.Add(new ActualIndex(table, name, unique, [.. columns]));
            }
        }

        var failures = new List<string>();

        foreach (var expected in _expected)
        {
            var match = actual.FirstOrDefault(a =>
                a.Table == expected.Table && a.Columns.SequenceEqual(expected.Columns));

            if (match is null)
            {
                failures.Add($"{expected.Table}({string.Join(",", expected.Columns)}): no index found on these columns in this order");
                continue;
            }

            if (match.Unique != expected.Unique)
            {
                failures.Add($"{expected.Table}({string.Join(",", expected.Columns)}): expected unique={expected.Unique}, got {match.Unique} (index '{match.Name}')");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
