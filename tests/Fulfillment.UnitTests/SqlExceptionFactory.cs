using System.Reflection;
using Microsoft.Data.SqlClient;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// <see cref="SqlException"/> has no public constructor — the driver builds
/// it only from a real TDS error response. This reflects into the same
/// internal factory the driver itself uses
/// (<c>SqlException.CreateException(SqlErrorCollection, string)</c>) so a
/// UNIT test can exercise <c>StockErrorMapper</c>'s <see cref="SqlException"/>
/// branches (deadlock 1205, lock-timeout 1222, and "any other" number)
/// without a real SQL Server — the deadlock/lock-timeout SHAPE is proven
/// against a REAL SQL Server separately, at integration level (`G6`); this
/// helper exists only to reach the mapper's own C# branch in isolation.
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
