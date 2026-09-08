using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>R54</c>/<c>PR20</c> — <c>src/Projector</c> is the only RUNTIME writer
/// of <c>order_timeline</c>; <c>src/Seed</c> is allow-listed BY NAME as an
/// offline fixture loader, never deployed, run before the system runs. Scans
/// PRODUCTION source only (never <c>tests/</c>). Two stated limitations: a
/// same-name unrelated method is a false positive; <c>$merge</c>/<c>$out</c>
/// inside an <c>Aggregate</c> call is a false negative — this guard does not
/// see either.
/// </summary>
public sealed partial class ReadModelSoleWriterTests
{
    /// <summary><c>PR20</c>'s enumerated MongoDB.Driver write-shaped method-name prefixes.</summary>
    private static readonly string[] _writeShapedMethodNames =
    [
        "InsertOne", "InsertMany", "UpdateOne", "UpdateMany", "ReplaceOne",
        "FindOneAndUpdate", "FindOneAndReplace", "FindOneAndDelete",
        "DeleteOne", "DeleteMany", "BulkWrite",
        "Indexes.CreateOne", "Indexes.CreateMany", "Drop",
    ];

    /// <summary>allow-listed by name — an offline fixture loader, never deployed, run before the system runs, writing documents in exactly the shape this feature specifies (design.md §10, L53).</summary>
    private static readonly string[] _allowListedServices = ["Projector", "Seed"];

    [Fact]
    public void R54_PermitsAReadModelWriteOnlyInTheProjectorAndTheAllowListedOfflineFixtureLoader_WhilePermittingAReadAnywhere()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var srcDir = Path.Combine(root, "src");

        var violations = new List<string>();
        var allowListedHitCounts = _allowListedServices.ToDictionary(s => s, _ => 0);

        foreach (var serviceDir in Directory.EnumerateDirectories(srcDir))
        {
            var serviceName = Path.GetFileName(serviceDir);
            var hits = ScanForWriteShapedCalls(serviceDir);

            if (_allowListedServices.Contains(serviceName))
            {
                allowListedHitCounts[serviceName] = hits.Count;
                continue;
            }

            violations.AddRange(hits.Select(h => $"{serviceName}: {h}"));
        }

        Assert.True(violations.Count == 0, "Write-shaped MongoDB.Driver call(s) outside the allow-list: " + string.Join("; ", violations));

        // Non-vacuity: both allow-listed services genuinely DO contain such
        // a call, so this guard cannot pass by matching nothing.
        Assert.True(allowListedHitCounts["Projector"] > 0, "Projector has no write-shaped call at all — the guard would be vacuous.");
        Assert.True(allowListedHitCounts["Seed"] > 0, "Seed has no write-shaped call at all — the guard would be vacuous.");
    }

    /// <summary><c>PR20</c> — proves non-vacuity on a SCRATCH tree, planting the fourth writer in a service the guard's own allow-list does not mention.</summary>
    [Fact]
    public void PR20_FiresWhenAThirdServiceAcquiresAWriteShapedCall_ProvedOnAScratchTree()
    {
        var scratchRoot = Directory.CreateTempSubdirectory("otc-readmodel-sole-writer-probe");
        try
        {
            var fakeServiceDir = Path.Combine(scratchRoot.FullName, "NotAllowListedService");
            Directory.CreateDirectory(fakeServiceDir);
            File.WriteAllText(
                Path.Combine(fakeServiceDir, "RogueWriter.cs"),
                "public sealed class RogueWriter { void Go() { collection.UpdateOneAsync(filter, update); } }");

            var hits = ScanForWriteShapedCalls(fakeServiceDir);
            Assert.NotEmpty(hits);
        }
        finally
        {
            scratchRoot.Delete(recursive: true);
        }
    }

    private static List<string> ScanForWriteShapedCalls(string serviceDir)
    {
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(serviceDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var methodName in _writeShapedMethodNames)
                {
                    if (WriteCallRegex(methodName).IsMatch(lines[i]))
                    {
                        hits.Add($"{Path.GetFileName(file)}:{i + 1} — {lines[i].Trim()}");
                    }
                }
            }
        }

        return hits;
    }

    private static Regex WriteCallRegex(string methodName) =>
        new(Regex.Escape(methodName) + @"(Async)?\s*[<(]");
}
