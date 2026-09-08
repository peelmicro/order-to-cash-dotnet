using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>PR24</c> — the THIRD banner line (<c>Behavioural conformance:</c>),
/// which <c>IdempotentConsumerParityTests</c> case 4 does NOT check. Arm
/// three ways: remove the canonical path from the banner; remove the
/// <c>Divergence:</c> line; point the <c>Behavioural conformance:</c> line
/// at a file that does not exist.
/// </summary>
public sealed class VariantBannerTests
{
    private const string ConsumerRelativePath = "src/Projector/Infrastructure/Messaging/IdempotentConsumer.cs";
    private const string CanonicalPathLiteral = "src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs";

    [Fact]
    public void PR24_TheBannerNamesTheCanonicalPath_ADivergenceLine_AndABehaviouralConformanceFileThatExists()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var content = File.ReadAllText(Path.Combine(root, ConsumerRelativePath));
        var banner = ExtractBanner(content);

        Assert.Contains(CanonicalPathLiteral, banner, StringComparison.Ordinal);
        Assert.Contains("Divergence:", banner, StringComparison.Ordinal);

        var conformanceLine = banner.Split('\n').FirstOrDefault(l => l.Contains("Behavioural conformance:", StringComparison.Ordinal));
        Assert.NotNull(conformanceLine);

        var afterLabel = conformanceLine![(conformanceLine.IndexOf("Behavioural conformance:", StringComparison.Ordinal) + "Behavioural conformance:".Length)..];
        var conformancePath = afterLabel.Trim();
        Assert.True(File.Exists(Path.Combine(root, conformancePath)), $"Behavioural conformance file '{conformancePath}' does not exist.");
    }

    private static string ExtractBanner(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bannerLines = lines.TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal));
        return string.Join('\n', bannerLines);
    }
}
