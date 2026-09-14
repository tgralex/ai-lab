using System.Runtime.CompilerServices;

namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>Minimal Server-Sent-Events parser: "event:"/"data:" fields, blank line terminates an event.</summary>
public static class SseParser
{
    public static async IAsyncEnumerable<SseEvent> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var dataLines = new List<string>();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (dataLines.Count > 0 || eventName is not null)
                {
                    yield return new SseEvent { EventName = eventName, Data = string.Join("\n", dataLines) };
                }

                eventName = null;
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line["data:".Length..].Trim());
            }
            // other fields (id:, retry:, ':' comments) are ignored — not used by the Responses API
        }

        if (dataLines.Count > 0 || eventName is not null)
        {
            yield return new SseEvent { EventName = eventName, Data = string.Join("\n", dataLines) };
        }
    }
}
