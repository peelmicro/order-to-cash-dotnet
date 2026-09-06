using System.Text.RegularExpressions;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// Reads <c>specs/shared/asyncapi.yaml</c> as TEXT — the
/// <c>RpcSubjectsTests</c>/<c>OrdersFactTopicTests</c> block-extraction
/// technique, generalised to <c>components.schemas</c> — and returns
/// declared property names (in declaration order) or enum values. Backlog
/// id 51, `BC23` (design.md §10.2): the ONE place these are parsed from the
/// spec, so <c>CreditRpcPayloadTests</c>, <c>StockRpcPayloadTests</c>,
/// <c>SagaCommandPayloadTests</c> and <c>OrdersCreateErrorMapperTests</c>
/// can each assert their own key/code list against it instead of retyping
/// a second copy of the contract.
/// </summary>
public static partial class AsyncApiSchema
{
    private static readonly string _specPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));

    /// <summary>Reads the given spec file's <c>components.schemas.&lt;schemaName&gt;</c> property names, in declaration order. Resolves ONE level of <c>allOf</c> (<c>CreditListRequestPayload</c> needs it): a list item that is itself a bare <c>$ref</c> is recursively expanded; an inline <c>type: object</c> list item contributes its own <c>properties:</c>.</summary>
    public static IReadOnlyList<string> PropertyNamesOf(string schemaName) => PropertyNamesOf(File.ReadAllText(_specPath), schemaName);

    /// <summary>Overload for a caller pointing at a scratch copy of the spec (`G5`'s arming) — never the real, read-only <c>specs/shared/asyncapi.yaml</c>.</summary>
    public static IReadOnlyList<string> PropertyNamesOf(string specText, string schemaName)
    {
        var lines = SplitLines(specText);
        var block = ExtractSchemaBlockLines(lines, schemaName);

        var names = new List<string>();

        // allOf: a bare "- $ref: '#/components/schemas/X'" list item,
        // DIRECTLY under the schema's OWN "allOf:" key (never a $ref
        // nested inside one of this schema's own properties, e.g.
        // CreditHoldRequestPayload.amount's `allOf: [$ref: Money]` —
        // that is a property's type, not this schema's shape), is
        // resolved recursively; other list items (typically inline
        // "type: object" additions) fall through to the properties scan
        // below, which finds their nested `properties:` block too.
        foreach (var refSchemaName in ExtractOwnAllOfRefs(block))
        {
            names.AddRange(PropertyNamesOf(specText, refSchemaName));
        }

        names.AddRange(ExtractPropertyNames(block));

        return names;
    }

    /// <summary>Reads <c>&lt;schemaName&gt;.&lt;propertyName&gt;</c>'s declared <c>enum:</c> values, in declaration order — e.g. <c>EnumValuesOf("RpcError.code")</c>.</summary>
    public static IReadOnlyList<string> EnumValuesOf(string schemaPath) => EnumValuesOf(File.ReadAllText(_specPath), schemaPath);

    public static IReadOnlyList<string> EnumValuesOf(string specText, string schemaPath)
    {
        var parts = schemaPath.Split('.', 2);
        var lines = SplitLines(specText);
        var block = ExtractSchemaBlockLines(lines, parts[0]);

        if (parts.Length > 1)
        {
            block = ExtractPropertyBlockLines(block, parts[1]);
        }

        return ExtractEnumValues(block);
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>The schema's block: from its own 4-space-indented <c>&lt;name&gt;:</c> line up to (not including) the next 4-space-indented sibling key.</summary>
    private static List<string> ExtractSchemaBlockLines(string[] lines, string schemaName)
    {
        var startIndex = Array.FindIndex(lines, line => line == $"    {schemaName}:");
        if (startIndex < 0)
        {
            throw new InvalidOperationException($"could not locate the '{schemaName}:' schema block in specs/shared/asyncapi.yaml (or the scratch copy under test).");
        }

        var endIndex = lines.Length;
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (FourSpaceKeyRegex().IsMatch(lines[i]))
            {
                endIndex = i;
                break;
            }
        }

        return [.. lines[startIndex..endIndex]];
    }

    /// <summary>Within an already-extracted block, the sub-block for one named property — from its own <c>&lt;indent&gt;&lt;propertyName&gt;:</c> line to the next sibling at the same indent.</summary>
    private static List<string> ExtractPropertyBlockLines(List<string> block, string propertyName)
    {
        var startIndex = block.FindIndex(line => PropertyKeyRegex().IsMatch(line) && PropertyKeyRegex().Match(line).Groups[1].Value == propertyName);
        if (startIndex < 0)
        {
            throw new InvalidOperationException($"could not locate the '{propertyName}:' property inside the schema block.");
        }

        var indent = LeadingSpaces(block[startIndex]);
        var endIndex = block.Count;
        for (var i = startIndex + 1; i < block.Count; i++)
        {
            if (block[i].Trim().Length == 0)
            {
                continue;
            }

            if (LeadingSpaces(block[i]) <= indent)
            {
                endIndex = i;
                break;
            }
        }

        return block[startIndex..endIndex];
    }

    /// <summary>Finds the schema's OWN <c>allOf:</c> key (a direct child of the schema, at the schema's indent + 2) and returns the schema names of every list item directly under it that is a bare <c>$ref</c> — never a <c>$ref</c> nested inside one of the schema's own properties.</summary>
    private static List<string> ExtractOwnAllOfRefs(List<string> block)
    {
        var schemaIndent = LeadingSpaces(block[0]);
        var allOfIndent = schemaIndent + 2;

        var allOfIndex = block.FindIndex(line => LeadingSpaces(line) == allOfIndent && line.Trim() == "allOf:");
        if (allOfIndex < 0)
        {
            return [];
        }

        var itemIndent = allOfIndent + 2;
        var refs = new List<string>();

        for (var i = allOfIndex + 1; i < block.Count; i++)
        {
            var line = block[i];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = LeadingSpaces(line);
            if (indent < itemIndent)
            {
                break;
            }

            if (indent == itemIndent)
            {
                var match = AllOfRefRegex().Match(line);
                if (match.Success)
                {
                    refs.Add(match.Groups[1].Value);
                }
            }
        }

        return refs;
    }

    /// <summary>
    /// Returns the schema's OWN top-level property names — the
    /// SHALLOWEST-indented <c>properties:</c> line in the block (there is
    /// exactly one: either the schema's direct <c>properties:</c>, or, for
    /// an <c>allOf</c> schema, the inline object list item's own). Deeper
    /// <c>properties:</c> lines belong to a NESTED object schema (e.g. an
    /// array property's <c>items: {type: object, properties: {...}}</c>)
    /// and must NOT be flattened into this schema's own key list.
    /// </summary>
    private static List<string> ExtractPropertyNames(List<string> block)
    {
        var propertiesLineIndices = block
            .Select((line, index) => (line, index))
            .Where(t => t.line.Trim() == "properties:")
            .ToList();

        if (propertiesLineIndices.Count == 0)
        {
            return [];
        }

        var shallowestIndent = propertiesLineIndices.Min(t => LeadingSpaces(t.line));
        var i = propertiesLineIndices.First(t => LeadingSpaces(t.line) == shallowestIndent).index;

        var names = new List<string>();
        {
            var propertyIndent = LeadingSpaces(block[i]) + 2;

            for (var j = i + 1; j < block.Count; j++)
            {
                var line = block[j];
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                var indent = LeadingSpaces(line);
                if (indent < propertyIndent)
                {
                    break;
                }

                if (indent == propertyIndent)
                {
                    var match = PropertyKeyRegex().Match(line);
                    if (match.Success)
                    {
                        names.Add(match.Groups[1].Value);
                    }
                }
            }
        }

        return names;
    }

    private static List<string> ExtractEnumValues(List<string> block)
    {
        var enumIndex = block.FindIndex(line => line.Trim() == "enum:");
        if (enumIndex < 0)
        {
            throw new InvalidOperationException("could not locate an 'enum:' list in the given block.");
        }

        var values = new List<string>();
        var itemIndent = LeadingSpaces(block[enumIndex]) + 2;

        for (var i = enumIndex + 1; i < block.Count; i++)
        {
            var line = block[i];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = LeadingSpaces(line);
            if (indent < itemIndent)
            {
                break;
            }

            var match = EnumItemRegex().Match(line);
            if (match.Success)
            {
                values.Add(match.Groups[1].Value.Trim('\'', '"'));
            }
        }

        return values;
    }

    private static int LeadingSpaces(string line) => line.Length - line.TrimStart(' ').Length;

    [GeneratedRegex(@"^    (\w+):$")]
    private static partial Regex FourSpaceKeyRegex();

    [GeneratedRegex(@"^\s*(\w+):")]
    private static partial Regex PropertyKeyRegex();

    [GeneratedRegex(@"^\s*-\s*\$ref:\s*'#/components/schemas/(\w+)'\s*$", RegexOptions.Multiline)]
    private static partial Regex AllOfRefRegex();

    [GeneratedRegex(@"^\s*-\s*(\S+)\s*$")]
    private static partial Regex EnumItemRegex();
}
