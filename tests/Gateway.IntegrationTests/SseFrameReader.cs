using System.Text;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// A minimal raw SSE frame parser/collector over an already-open response
/// stream — ported from #7's <c>test-support/sse-test-client.ts</c>
/// (<c>parseSseFrames</c>/<c>collectUntil</c>). Deliberately NOT
/// <c>System.Net.ServerSentEvents</c>-based (that BCL parser only ships
/// from .NET 10's own <c>SseParser</c>, but hides exactly the
/// header/frame-shape detail this suite exists to assert on — the raw
/// <c>id:</c>/<c>event:</c>/<c>data:</c> block shape, and specifically
/// whether a given block carries an <c>id:</c> line AT ALL, which a
/// higher-level parser that always exposes an <c>Id</c> property cannot
/// distinguish "absent" from "empty string" for).
/// </summary>
public sealed record SseFrame(string? Id, string Event, string DataJson);

public sealed class SseFrameReader(Stream stream)
{
    private readonly StringBuilder _buffer = new();
    private readonly List<SseFrame> _collected = [];
    private readonly byte[] _readBuffer = new byte[8192];

    public IReadOnlyList<SseFrame> Collected => _collected;

    /// <summary>Reads chunks from the underlying stream until <paramref name="predicate"/> is satisfied by the accumulated frame list, or <paramref name="timeout"/> elapses.</summary>
    public async Task<IReadOnlyList<SseFrame>> CollectUntilAsync(
        Func<IReadOnlyList<SseFrame>, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (predicate(_collected))
        {
            return [.. _collected];
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(_readBuffer, cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("SSE stream ended before the predicate was satisfied.");
                }

                _buffer.Append(Encoding.UTF8.GetString(_readBuffer, 0, read));
                DrainFrames();

                if (predicate(_collected))
                {
                    return [.. _collected];
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"collectUntil: timed out after {timeout}, collected so far: {_collected.Count} frame(s).");
        }
    }

    private void DrainFrames()
    {
        var text = _buffer.ToString();
        var blocks = text.Split("\n\n");

        for (var i = 0; i < blocks.Length - 1; i++)
        {
            var block = blocks[i];
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            string? id = null;
            string? eventType = null;
            string? data = null;

            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = line["id: ".Length..];
                }
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventType = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
            }

            if (eventType is not null && data is not null)
            {
                _collected.Add(new SseFrame(id, eventType, data));
            }
        }

        _buffer.Clear();
        _buffer.Append(blocks[^1]);
    }
}
