using System.Runtime.CompilerServices;
using System.Text;

namespace AiLab.Infrastructure.Sse;

/// <summary>
/// Minimal hand-written text/event-stream parser (event:/data: lines, blank line terminates an
/// event) — shared by OpenAiProvider and AnthropicProvider. Ported from the WinForms SseParser.
/// </summary>
public static class SseParser
{
    public static async IAsyncEnumerable<SseEvent> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? eventName = null;
        var dataBuilder = new StringBuilder();
        var hasData = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (hasData)
                {
                    yield return new SseEvent { EventName = eventName, Data = dataBuilder.ToString() };
                }

                eventName = null;
                dataBuilder.Clear();
                hasData = false;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (hasData)
                {
                    dataBuilder.Append('\n');
                }

                dataBuilder.Append(line["data:".Length..].TrimStart());
                hasData = true;
            }
            // Other SSE fields (id:, retry:, comments starting with ':') are ignored — not needed here.
        }

        // A stream that ends without a trailing blank line still carries a final event.
        if (hasData)
        {
            yield return new SseEvent { EventName = eventName, Data = dataBuilder.ToString() };
        }
    }
}
