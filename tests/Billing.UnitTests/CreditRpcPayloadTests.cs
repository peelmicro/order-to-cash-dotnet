using System.Text.Json;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BC23` (backlog id 51, design.md §10.2) — every <c>billing.credit.*</c>
/// request and reply record carries EXACTLY the property names
/// <c>asyncapi.yaml</c> declares, parsed from the spec, never retyped. Also
/// ledger `L18` (`F3`): the exact key SET per reply <c>outcome</c> —
/// optional fields are OMITTED, never sent as <c>null</c>.
/// </summary>
public sealed class CreditRpcPayloadTests
{
    public static TheoryData<string, Type> RequestAndReplySchemas() => new()
    {
        { "CreditHoldRequestPayload", typeof(CreditHoldRequestPayload) },
        { "CreditHoldReplyPayload", typeof(CreditHoldReplyPayload) },
        { "CreditReleaseRequestPayload", typeof(CreditReleaseRequestPayload) },
        { "CreditReleaseReplyPayload", typeof(CreditReleaseReplyPayload) },
        { "CreditListRequestPayload", typeof(CreditListRequestPayload) },
        { "CreditListReplyPayload", typeof(CreditListReplyPayload) },
        { "Money", typeof(CreditMoney) },
    };

    [Theory]
    [MemberData(nameof(RequestAndReplySchemas))]
    public void BC23_EveryCreditRequestAndReplyRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(string schemaName, Type payloadType)
    {
        var expected = AsyncApiSchema.PropertyNamesOf(schemaName).ToHashSet(StringComparer.Ordinal);
        var actual = payloadType.GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void F3_AnApprovedReplyCarriesExactlyItsSixKeys_AndNoReasonKey()
    {
        var reply = new CreditHoldReplyPayload("approved", "ORD-000001", "EUR", 9_000, "CR-000001", 1_000);

        var json = JsonSerializer.SerializeToElement(reply, OrderToCash.Contracts.Wire.JsonWire.Options);
        var keys = json.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "outcome", "orderReference", "currency", "availableCredit", "creditCode", "heldAmount" }, keys);
        Assert.DoesNotContain("reason", keys);
    }

    [Fact]
    public void F3_ARejectedReplyCarriesReason_AndNoHeldAmountKey()
    {
        var reply = new CreditHoldReplyPayload("rejected", "ORD-000001", "EUR", 10_000, "CR-000001", Reason: "over_limit");

        var json = JsonSerializer.SerializeToElement(reply, OrderToCash.Contracts.Wire.JsonWire.Options);
        var keys = json.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("reason", keys);
        Assert.DoesNotContain("heldAmount", keys);
    }

    /// <summary>
    /// `G5` — the arming that proves `BC23`'s guard has teeth. A SCRATCH
    /// copy of the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with <c>CreditHoldReplyPayload</c>'s
    /// <c>orderReference</c> renamed: parsing the scratch copy no longer
    /// agrees with <see cref="CreditHoldReplyPayload"/>'s actual property
    /// set — exactly what would make the real
    /// <see cref="BC23_EveryCreditRequestAndReplyRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped"/>
    /// case fail if the spec really changed this way.
    /// </summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWhoseCreditHoldReplyPayloadPropertyWasRenamed()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace("        orderReference:\n          $ref: '#/components/schemas/OrderReference'\n        creditCode:\n          $ref: '#/components/schemas/CreditCode'\n        currency:\n          $ref: '#/components/schemas/CurrencyCode'\n        heldAmount:",
                          "        orderReferenceRenamed:\n          $ref: '#/components/schemas/OrderReference'\n        creditCode:\n          $ref: '#/components/schemas/CreditCode'\n        currency:\n          $ref: '#/components/schemas/CurrencyCode'\n        heldAmount:", StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.PropertyNamesOf(scratchText, "CreditHoldReplyPayload").ToHashSet(StringComparer.Ordinal);
            var actual = typeof(CreditHoldReplyPayload).GetProperties().Select(ToCamelCase).ToHashSet(StringComparer.Ordinal);

            Assert.NotEqual(actual, parsedFromScratch);
            Assert.DoesNotContain("orderReference", parsedFromScratch);
            Assert.Contains("orderReferenceRenamed", parsedFromScratch);
        }
        finally
        {
            File.Delete(scratchPath);
        }
    }

    private static string ToCamelCase(System.Reflection.PropertyInfo property) =>
        string.Concat(char.ToLowerInvariant(property.Name[0]).ToString(), property.Name[1..]);
}
