using System.Text.Json;
using Xunit;

namespace OrderToCash.Contracts.UnitTests;

/// <summary>
/// Deep-equality assertion over two JSON documents that treats object key
/// ORDER as immaterial and array element order as significant — exactly the
/// distinction CLAUDE.md draws for the payload of a fact envelope: "same
/// keys, same values, same types, same casing; key order is NOT asserted".
/// Used wherever this test project asserts payload semantic equality or the
/// payload half of a round trip; envelope-level byte-exactness (field SET
/// and ORDER, and the exact bytes of the six scalar fields) is asserted
/// separately and does care about order — see
/// <c>GoldenEnvelopeParityTests</c>.
/// </summary>
internal static class JsonEquivalence
{
    public static void AssertSemanticallyEqual(JsonElement expected, JsonElement actual, string path = "$")
    {
        Assert.True(
            expected.ValueKind == actual.ValueKind
                // System.Text.Json has no separate "integer" kind — both
                // integers and decimals report Number, which is exactly
                // right here: MinorUnits (R1) is defined as never carrying a
                // decimal representation, so this comparison never needs to
                // distinguish "124250" from "124250.0" as different kinds.
                || (IsNumber(expected.ValueKind) && IsNumber(actual.ValueKind)),
            $"{path}: kind mismatch — expected {expected.ValueKind}, found {actual.ValueKind}");

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                AssertObjectsSemanticallyEqual(expected, actual, path);
                break;

            case JsonValueKind.Array:
                AssertArraysSemanticallyEqual(expected, actual, path);
                break;

            case JsonValueKind.String:
                Assert.True(
                    string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
                    $"{path}: expected \"{expected.GetString()}\", found \"{actual.GetString()}\"");
                break;

            case JsonValueKind.Number:
                Assert.True(
                    expected.GetRawText() == actual.GetRawText()
                        || expected.GetDecimal() == actual.GetDecimal(),
                    $"{path}: expected {expected.GetRawText()}, found {actual.GetRawText()}");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                // Kind equality (asserted above) is the whole comparison for
                // these leaf kinds.
                break;

            default:
                throw new InvalidOperationException($"{path}: unexpected JSON value kind {expected.ValueKind}.");
        }
    }

    private static bool IsNumber(JsonValueKind kind) => kind == JsonValueKind.Number;

    private static void AssertObjectsSemanticallyEqual(JsonElement expected, JsonElement actual, string path)
    {
        var expectedProperties = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var actualProperties = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

        var missingFromActual = expectedProperties.Keys.Except(actualProperties.Keys, StringComparer.Ordinal).ToArray();
        var unexpectedInActual = actualProperties.Keys.Except(expectedProperties.Keys, StringComparer.Ordinal).ToArray();

        Assert.True(
            missingFromActual.Length == 0 && unexpectedInActual.Length == 0,
            $"{path}: key set differs — missing {{{string.Join(", ", missingFromActual)}}}, " +
            $"unexpected {{{string.Join(", ", unexpectedInActual)}}}");

        foreach (var (key, expectedValue) in expectedProperties)
        {
            AssertSemanticallyEqual(expectedValue, actualProperties[key], $"{path}.{key}");
        }
    }

    private static void AssertArraysSemanticallyEqual(JsonElement expected, JsonElement actual, string path)
    {
        var expectedItems = expected.EnumerateArray().ToArray();
        var actualItems = actual.EnumerateArray().ToArray();

        Assert.True(
            expectedItems.Length == actualItems.Length,
            $"{path}: array length differs — expected {expectedItems.Length}, found {actualItems.Length}");

        for (var i = 0; i < expectedItems.Length; i++)
        {
            AssertSemanticallyEqual(expectedItems[i], actualItems[i], $"{path}[{i}]");
        }
    }
}
