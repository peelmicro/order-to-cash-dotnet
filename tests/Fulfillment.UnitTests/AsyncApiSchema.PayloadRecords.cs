using System.Reflection;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// Backlog id 70 (<c>retyped_key_list_guards_survive_in_three_more_payload_test_files</c>)
/// — the two assertions a payload-key theory row makes, in the ONE place
/// each is written, so no test file retypes either of them.
///
/// <para><b>Why reflection and not a hand-typed key list.</b> Backlog id 64
/// retired the hand-retyped list in one file; the same shape survived in
/// three more. A hand-typed list cannot notice an ADDED property, because
/// <see cref="OrderToCash.Contracts.Wire.JsonWire"/> OMITS a null optional
/// from the wire — so a serialised-keys assertion never sees it either, and
/// a list compared against the spec compares two pieces of text that both
/// know nothing about the record. Measured, on this repository, before this
/// file existed: an undeclared property on a reply record left the whole
/// unit suite green. Reading the record's own properties is the only form
/// of the assertion that can fail for that defect.</para>
///
/// <para><b>Why the schema NAME needs its own assertion.</b> A key-set
/// comparison cannot guard WHICH schema a row claims wherever two real
/// sibling schemas declare the same keys — <c>StockCheckRequestPayload</c>
/// and <c>StockReplenishRequestPayload</c> are both
/// <c>{ companyCode, lines }</c>, so transposing those two row labels is
/// invisible to the key set and to everything else in the suite.
/// <see cref="AssertTheRowsSchemaNameNamesTheRecordItClaims"/> closes that
/// by deriving the agreement from the two names themselves rather than
/// from a second hand-typed mapping.</para>
/// </summary>
public static partial class AsyncApiSchema
{
    /// <summary>The record's OWN public property names, camelCased — read by reflection from the type, never retyped. The shape <c>tests/Billing.UnitTests/CreditRpcPayloadTests.cs</c> established.</summary>
    public static HashSet<string> PropertyNamesOfRecord(Type payloadType) =>
        payloadType.GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The record declared for <paramref name="schemaName"/> carries
    /// EXACTLY the property names <c>asyncapi.yaml</c> declares for it —
    /// no more (an undeclared extra) and no fewer (a dropped optional).
    /// The failure message NAMES both differences; xUnit's own
    /// <c>Assert.Equal</c> on two <see cref="HashSet{T}"/>s truncates the
    /// two sets with an ellipsis and therefore does not.
    /// </summary>
    public static void AssertRecordCarriesExactlyTheSchemasProperties(string schemaName, Type payloadType)
    {
        var declaredBySchema = PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var declaredByRecord = PropertyNamesOfRecord(payloadType);

        if (declaredBySchema.SetEquals(declaredByRecord))
        {
            return;
        }

        var missingFromRecord = declaredBySchema.Except(declaredByRecord).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var undeclaredBySchema = declaredByRecord.Except(declaredBySchema).OrderBy(key => key, StringComparer.Ordinal).ToList();

        Assert.Fail(
            $"record {payloadType.FullName} does not carry exactly the property names asyncapi.yaml's '{schemaName}' schema declares. " +
            $"Declared by the schema but MISSING from the record: [{string.Join(", ", missingFromRecord)}]. " +
            $"Declared by the record but UNDECLARED by the schema: [{string.Join(", ", undeclaredBySchema)}].");
    }

    /// <summary>
    /// The schema name a theory row claims actually names the record the
    /// same row supplies. The agreement is DERIVED from the two names —
    /// the record's simple name is the schema name, optionally suffixed
    /// with <c>Payload</c> and optionally prefixed with a service or
    /// subject qualifier (<c>PageInfo</c> → <c>StockPageInfo</c>,
    /// <c>Money</c> → <c>CreditMoney</c>, <c>Product</c> →
    /// <c>ProductPayload</c>) — never from a second hand-typed mapping,
    /// which would be the very thing id 70 retires.
    /// </summary>
    public static void AssertTheRowsSchemaNameNamesTheRecordItClaims(string schemaName, Type payloadType)
    {
        var recordName = payloadType.Name;
        var agrees = recordName.EndsWith(schemaName, StringComparison.Ordinal)
            || recordName.EndsWith(schemaName + "Payload", StringComparison.Ordinal);

        Assert.True(
            agrees,
            $"this theory row claims asyncapi.yaml schema '{schemaName}' for record {payloadType.FullName}, but '{recordName}' does not name that schema " +
            $"(expected the record's name to end with '{schemaName}'{(schemaName.EndsWith("Payload", StringComparison.Ordinal) ? string.Empty : $" or '{schemaName}Payload'")}, optionally prefixed with a service or subject qualifier). " +
            "A row whose schema name is substituted for a real sibling's is invisible to the key-set assertion whenever the two schemas declare the same keys.");
    }

    private static string ToCamelCase(PropertyInfo property) =>
        string.Concat(char.ToLowerInvariant(property.Name[0]).ToString(), property.Name[1..]);
}
