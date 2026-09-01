using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Review D1 (feature db_orders, rejected first pass): Databases doc
/// §4.1/§4.2 annotate `currency_id`, `company_id`, `retailer_id` and
/// `product_id` as `FK -&gt; ...` on every table that carries them, and §3
/// states the rule directly — "Foreign keys are used freely inside one
/// database ... and never across databases." This asserts the **exact**
/// set of eight foreign keys #7's committed
/// `apps/orders/drizzle/0000_bizarre_champions.sql:114-121` emits, read
/// from <c>sys.foreign_keys</c>/<c>sys.foreign_key_columns</c> on the real
/// database — never from EF's own model, which is exactly what let seven
/// of the eight go missing the first time with a fully green suite.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class ForeignKeyTests(MsSqlContainerFixture fixture)
{
    private sealed record ExpectedForeignKey(
        string Table,
        string Column,
        string ReferencedTable,
        string ReferencedColumn,
        string DeleteAction);

    private static readonly ExpectedForeignKey[] _expected =
    [
        new("companies", "currency_id", "currencies", "id", "NO_ACTION"),
        new("products", "currency_id", "currencies", "id", "NO_ACTION"),
        new("retailers", "currency_id", "currencies", "id", "NO_ACTION"),
        new("orders", "company_id", "companies", "id", "NO_ACTION"),
        new("orders", "currency_id", "currencies", "id", "NO_ACTION"),
        new("orders", "retailer_id", "retailers", "id", "NO_ACTION"),
        new("order_items", "order_id", "orders", "id", "CASCADE"),
        new("order_items", "product_id", "products", "id", "NO_ACTION"),
    ];

    private sealed record ActualForeignKey(
        string Table,
        string Column,
        string ReferencedTable,
        string ReferencedColumn,
        string DeleteAction);

    [Fact]
    public async Task Exactly_The_Eight_Spec_ForeignKeys_Exist_With_The_Right_Reference_And_DeleteAction()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_fk_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var actual = new List<ActualForeignKey>();

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    parent.name AS parent_table,
                    parent_col.name AS parent_column,
                    ref.name AS referenced_table,
                    ref_col.name AS referenced_column,
                    fk.delete_referential_action_desc
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc
                    ON fkc.constraint_object_id = fk.object_id
                JOIN sys.tables parent ON parent.object_id = fk.parent_object_id
                JOIN sys.columns parent_col
                    ON parent_col.object_id = fkc.parent_object_id AND parent_col.column_id = fkc.parent_column_id
                JOIN sys.tables ref ON ref.object_id = fk.referenced_object_id
                JOIN sys.columns ref_col
                    ON ref_col.object_id = fkc.referenced_object_id AND ref_col.column_id = fkc.referenced_column_id
                ORDER BY parent.name, parent_col.name;
                """;

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actual.Add(new ActualForeignKey(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        var failures = new List<string>();

        foreach (var expected in _expected)
        {
            var match = actual.FirstOrDefault(a => a.Table == expected.Table && a.Column == expected.Column);

            if (match is null)
            {
                failures.Add($"{expected.Table}.{expected.Column}: no foreign key found");
                continue;
            }

            if (match.ReferencedTable != expected.ReferencedTable || match.ReferencedColumn != expected.ReferencedColumn)
            {
                failures.Add(
                    $"{expected.Table}.{expected.Column}: expected reference {expected.ReferencedTable}.{expected.ReferencedColumn}, " +
                    $"got {match.ReferencedTable}.{match.ReferencedColumn}");
            }

            if (match.DeleteAction != expected.DeleteAction)
            {
                failures.Add($"{expected.Table}.{expected.Column}: expected delete action {expected.DeleteAction}, got {match.DeleteAction}");
            }
        }

        var unexpected = actual
            .Where(a => !_expected.Any(e => e.Table == a.Table && e.Column == a.Column))
            .Select(a => $"{a.Table}.{a.Column}")
            .ToList();

        if (unexpected.Count > 0)
        {
            failures.Add($"unexpected foreign key(s) not in the spec: [{string.Join(", ", unexpected)}]");
        }

        Assert.Equal(8, actual.Count);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
