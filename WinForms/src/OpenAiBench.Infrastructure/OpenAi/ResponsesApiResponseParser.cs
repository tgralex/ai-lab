using System.Text;
using System.Text.Json;
using OpenAiBench.Core.Domain;

namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>
/// Maps a Responses API "response" JSON object (from the non-streaming body, or from a
/// response.completed/incomplete/failed SSE event's "response" field — same shape either way)
/// onto an <see cref="ExecutionRun"/>.
/// </summary>
public static class ResponsesApiResponseParser
{
    public static void ApplyResponseJson(ExecutionRun run, JsonElement response)
    {
        run.ResponseId = GetString(response, "id");
        run.ActualModel = GetString(response, "model");
        run.FinishReason = GetString(response, "status");
        run.Output = ExtractOutputText(response);
        run.Usage = ExtractUsage(response);

        if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            run.ApiErrorBody = error.GetRawText();
        }
    }

    private static string ExtractOutputText(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) &&
                    type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static TokenUsage ExtractUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return TokenUsage.Empty;
        }

        var cachedTokens = usage.TryGetProperty("input_tokens_details", out var inputDetails)
            ? GetInt(inputDetails, "cached_tokens")
            : 0;

        var reasoningTokens = usage.TryGetProperty("output_tokens_details", out var outputDetails)
            ? GetInt(outputDetails, "reasoning_tokens")
            : 0;

        return new TokenUsage
        {
            InputTokens = GetInt(usage, "input_tokens"),
            CachedInputTokens = cachedTokens,
            OutputTokens = GetInt(usage, "output_tokens"),
            ReasoningTokens = reasoningTokens,
            TotalTokens = GetInt(usage, "total_tokens")
        };
    }

    internal static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static int GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
