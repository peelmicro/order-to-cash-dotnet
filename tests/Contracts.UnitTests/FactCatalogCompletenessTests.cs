using System.Text.RegularExpressions;
using OrderToCash.Contracts.Facts;
using Xunit;

namespace OrderToCash.Contracts.UnitTests;

/// <summary>
/// Feature 8 acceptance item 1: "a test asserting every spec-declared fact
/// type AND required field is represented — parsing the spec, not a
/// hardcoded list of [N]. A hardcoded list cannot notice a spec change."
/// This class therefore reads specs/shared/asyncapi.yaml directly, twice:
/// once to extract every <c>eventType: const: &lt;fact&gt;</c> value under
/// <c>components.schemas.*Event</c> (the fact-type half), and once per
/// payload schema to extract its <c>required:</c> field list (the
/// required-field half), and checks both against
/// <see cref="FactCatalog.PayloadTypesByEventType"/> and reflection over its
/// CLR types respectively.
/// </summary>
public sealed partial class FactCatalogCompletenessTests
{
    [Fact]
    public void EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType()
    {
        var asyncApiPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var asyncApiText = File.ReadAllText(asyncApiPath);

        var declaredEventTypes = EventTypeConstRegex()
            .Matches(asyncApiText)
            .Select(m => m.Groups["eventType"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declaredEventTypes.Count > 0, "Parsed zero fact eventTypes from asyncapi.yaml — the regex or the file path is wrong, and every assertion below would be vacuously true over an empty set.");

        var catalogEventTypes = FactCatalog.PayloadTypesByEventType.Keys.ToHashSet(StringComparer.Ordinal);

        var missingFromCatalog = declaredEventTypes.Except(catalogEventTypes).ToArray();
        var extraInCatalog = catalogEventTypes.Except(declaredEventTypes).ToArray();

        Assert.True(
            missingFromCatalog.Length == 0,
            $"asyncapi.yaml declares fact type(s) with no representing type in FactCatalog: {string.Join(", ", missingFromCatalog)}");

        Assert.True(
            extraInCatalog.Length == 0,
            $"FactCatalog declares fact type(s) not present in asyncapi.yaml: {string.Join(", ", extraInCatalog)}");
    }

    /// <summary>
    /// The second half of acceptance item 1 — "and required field". For
    /// every payload type in <see cref="FactCatalog"/>, this locates that
    /// exact schema name (e.g. <c>OrderPlacedPayload:</c>) inside
    /// asyncapi.yaml, reads its <c>required:</c> list, and asserts the CLR
    /// type has a public property for each required field — comparing the
    /// spec's camelCase field name against the property's PascalCase name
    /// case-insensitively, which is the same transform
    /// <c>JsonNamingPolicy.CamelCase</c> performs in reverse.
    /// </summary>
    [Fact]
    public void EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields()
    {
        var asyncApiPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var asyncApiLines = File.ReadAllLines(asyncApiPath);

        var offences = new List<string>();

        foreach (var (eventType, payloadType) in FactCatalog.PayloadTypesByEventType)
        {
            var requiredFields = FindRequiredFields(asyncApiLines, payloadType.Name);

            Assert.True(
                requiredFields.Count > 0,
                $"Parsed zero required fields for schema '{payloadType.Name}' ({eventType}) — the schema name or the block parser is wrong.");

            var propertyNames = payloadType.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var requiredField in requiredFields)
            {
                var expectedPropertyName = char.ToUpperInvariant(requiredField[0]) + requiredField[1..];

                if (!propertyNames.Contains(expectedPropertyName))
                {
                    offences.Add($"{payloadType.Name} ({eventType}) has no property for required field '{requiredField}' (expected '{expectedPropertyName}')");
                }
            }
        }

        Assert.True(offences.Count == 0, string.Join("; ", offences));
    }

    /// <summary>
    /// Extracts the `required:` field-name list from one schema's block in
    /// asyncapi.yaml. Schemas are declared at 4-space indent
    /// (`    OrderPlacedPayload:`), their `required:` key at 6-space indent,
    /// and each field name as an 8-space-indented `- fieldName` list item —
    /// the exact, consistent shape verified against every payload schema in
    /// the file (see the read-through in
    /// progress/impl_contracts_package.md).
    /// </summary>
    private static List<string> FindRequiredFields(string[] lines, string schemaName)
    {
        var schemaHeader = $"    {schemaName}:";
        var startIndex = Array.FindIndex(lines, l => l == schemaHeader);

        if (startIndex < 0)
        {
            return [];
        }

        var endIndex = lines.Length;
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].Length > 4 && lines[i][4] != ' ' && lines[i].TrimStart().EndsWith(':'))
            {
                endIndex = i;
                break;
            }
        }

        var requiredFields = new List<string>();
        var inRequiredBlock = false;

        for (var i = startIndex + 1; i < endIndex; i++)
        {
            var line = lines[i];

            if (line == "      required:")
            {
                inRequiredBlock = true;
                continue;
            }

            if (!inRequiredBlock)
            {
                continue;
            }

            var match = RequiredFieldItemRegex().Match(line);
            if (match.Success)
            {
                requiredFields.Add(match.Groups["field"].Value);
            }
            else
            {
                break;
            }
        }

        return requiredFields;
    }

    // Matches the two-line shape asyncapi.yaml uses to pin one fact event
    // schema's eventType to one literal, e.g.:
    //   eventType:
    //     const: order.placed.v1
    [GeneratedRegex(@"eventType:\s*\r?\n\s*const:\s*(?<eventType>[a-z]+\.[a-z_]+\.v[0-9]+)\s*\r?\n")]
    private static partial Regex EventTypeConstRegex();

    // Matches one `required:` list item at 8-space indent, e.g. `        - orderReference`.
    [GeneratedRegex(@"^        - (?<field>[a-zA-Z][a-zA-Z0-9]*)$")]
    private static partial Regex RequiredFieldItemRegex();
}
