using System.Text;
using System.Text.Json;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Redirects <see cref="Console.Out"/> for the duration of one
/// <c>using</c> block and parses every captured line as JSON — copied from
/// <c>Orders.IntegrationTests.CapturedConsole</c> (review round 1, D2) so
/// <c>LogCorrelationTests</c> can capture <c>AddJsonConsole</c>'s REAL
/// output. MUST be redirected BEFORE the host is built — the console
/// logger provider captures the <see cref="TextWriter"/> reference once,
/// at construction time (probed directly against Orders' own host; see
/// the implementation record).
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
