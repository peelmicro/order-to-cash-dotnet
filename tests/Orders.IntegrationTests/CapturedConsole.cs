using System.Text;
using System.Text.Json;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Redirects <see cref="Console.Out"/> for the duration of one
/// <c>using</c> block and parses every captured line as JSON —
/// <c>LogCorrelationTests</c>' own capture mechanism for
/// <c>AddJsonConsole</c>'s real output. <see cref="Console.Out"/> is a
/// process-wide static, so a concurrently running, unrelated test's own
/// log lines can land in the same capture window; callers filter the
/// parsed records by their OWN flow's real correlationId rather than
/// relying on the capture window being exclusive.
/// </summary>
internal sealed class CapturedConsole : IDisposable
{
    private readonly StringWriter _writer = new(new StringBuilder());
    private TextWriter? _original;

    public IDisposable Redirect()
    {
        _original = Console.Out;
        Console.SetOut(_writer);
        return new Restorer(this);
    }

    public IReadOnlyList<JsonDocument> ParseJsonLines()
    {
        var text = _writer.ToString();
        var documents = new List<JsonDocument>();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim('\r', ' ');
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                continue;
            }

            try
            {
                documents.Add(JsonDocument.Parse(trimmed));
            }
            catch (JsonException)
            {
                // A line the JSON console formatter split across two
                // writes, or genuinely non-JSON console output from
                // elsewhere — skipped, not asserted on.
            }
        }

        return documents;
    }

    public void Dispose() => _writer.Dispose();

    private sealed class Restorer(CapturedConsole owner) : IDisposable
    {
        public void Dispose()
        {
            if (owner._original is not null)
            {
                Console.SetOut(owner._original);
            }
        }
    }
}
