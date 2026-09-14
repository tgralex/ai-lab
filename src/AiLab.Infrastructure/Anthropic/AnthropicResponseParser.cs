using System.Text;
using System.Text.Json;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Anthropic;

public sealed class ParsedAnthropicResponse
{
    public string? ResponseId { get; init; }

    public string? ActualModel { get; init; }

    public string? StopReason { get; init; }

    public string OutputText { get; init; } = string.Empty;

    public TokenUsage Usage { get; init; } = new();

    public string? ErrorMessage { get; init; }
}

/// <summary>Parses an Anthropic Messages API response object (non-streaming, or the accumulated state of a stream).</summary>
public static class AnthropicResponseParser
{
    public static ParsedAnthropicResponse Parse(JsonElement root)
    {
        var type = TryGetString(root, "type");
        if (type == "error")
        {
            var errorMessage = root.TryGetProperty("error", out var error) ? TryGetString(error, "message") : null;
            return new ParsedAnthropicResponse { ErrorMessage = errorMessage ?? "Anthropic returned an error." };
        }

        var responseId = TryGetString(root, "id");
        var actualModel = TryGetString(root, "model");
        var stopReason = TryGetString(root, "stop_reason");
        var outputText = ExtractOutputText(root);
        var usage = ExtractUsage(root);

        return new ParsedAnthropicResponse
        {
            ResponseId = responseId,
            ActualModel = actualModel,
            StopReason = stopReason,
            OutputText = outputText,
            Usage = usage,
        };
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            var blockType = TryGetString(block, "type");
            if (blockType == "text" && block.TryGetProperty("text", out var textElement))
            {
                builder.Append(textElement.GetString());
            }
            else if (blockType == "tool_use" && block.TryGetProperty("input", out var inputElement))
            {
                // Structured-output tool call: the tool's input *is* the structured output.
                builder.Append(inputElement.GetRawText());
            }
        }

        return builder.ToString();
    }

    public static TokenUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return new TokenUsage();
        }

        var inputTokens = TryGetInt(usage, "input_tokens") ?? 0;
        var outputTokens = TryGetInt(usage, "output_tokens") ?? 0;
        var cachedTokens = TryGetInt(usage, "cache_read_input_tokens") ?? 0;

        return new TokenUsage
        {
            InputTokens = inputTokens + cachedTokens, // Anthropic reports cache_read tokens separately from input_tokens; total input = both.
            CachedInputTokens = cachedTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = 0, // Anthropic doesn't break out extended-thinking tokens as a discrete usage field.
            TotalTokens = inputTokens + cachedTokens + outputTokens,
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? TryGetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
