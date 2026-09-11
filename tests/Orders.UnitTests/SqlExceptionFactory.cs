using System.Reflection;
using Microsoft.Data.SqlClient;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <see cref="SqlException"/> has no public constructor — the driver builds
/// it only from a real TDS error response. This reflects into the same
/// internal factory the driver itself uses
/// (<c>SqlException.CreateException(SqlErrorCollection, string)</c>) so a
/// UNIT test can exercise <c>RequestIdCollision.Matches</c>'s <see cref="SqlException"/>
/// branch without a real SQL Server — duplicated from
/// <c>tests/Billing.UnitTests/SqlExceptionFactory.cs</c> /
/// <c>tests/Fulfillment.UnitTests/SqlExceptionFactory.cs</c>, same shape.
/// The MESSAGE text this feature's tests pass in is captured verbatim from
/// a real <c>mssql</c> container (progress/impl_observability_reliability.md,
/// ledger L3) — this factory only reproduces the SHAPE of a real
/// <see cref="SqlException"/> around that captured text, never invents it.
/// </summary>
internal static class SqlExceptionFactory
{
    public static SqlException WithNumber(int number, string message = "stand-in SqlException")
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;

        var errorCtor = typeof(SqlError).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)],
            null)!;
        var error = errorCtor.Invoke([number, (byte)0, (byte)0, "test-server", message, "test-procedure", 1, null]);

        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!;
        addMethod.Invoke(collection, [error]);

        var createException = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(SqlErrorCollection), typeof(string)],
            null)!;

        return (SqlException)createException.Invoke(null, [collection, "11.0.0"])!;
    }
}
