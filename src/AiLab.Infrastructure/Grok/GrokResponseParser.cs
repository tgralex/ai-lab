using System.Text.Json;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Grok;

public sealed class ParsedGrokResponse
{
    public string? ResponseId { get; init; }

    public string? ActualModel { get; init; }

    public string? FinishReason { get; init; }

    public string OutputText { get; init; } = string.Empty;

    public TokenUsage Usage { get; init; } = new();

    public string? ErrorMessage { get; init; }
}

/// <summary>Parses an xAI Chat Completions response object (non-streaming, or the accumulated state of a stream).</summary>
public static class GrokResponseParser
{
    public static ParsedGrokResponse Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : error.ToString();
            return new ParsedGrokResponse { ErrorMessage = message ?? "Grok returned an error." };
        }

        var responseId = TryGetString(root, "id");
        var actualModel = TryGetString(root, "model");
        string? finishReason = null;
        var outputText = string.Empty;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            finishReason = TryGetString(choice, "finish_reason");
            if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                outputText = contentEl.GetString() ?? string.Empty;
            }
        }

        return new ParsedGrokResponse
        {
            ResponseId = responseId,
            ActualModel = actualModel,
            FinishReason = finishReason,
            OutputText = outputText,
            Usage = ExtractUsage(root),
        };
    }

    public static TokenUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new TokenUsage();
        }

        var inputTokens = TryGetInt(usage, "prompt_tokens") ?? 0;
        var outputTokens = TryGetInt(usage, "completion_tokens") ?? 0;
        var totalTokens = TryGetInt(usage, "total_tokens") ?? (inputTokens + outputTokens);

        var cachedTokens = 0;
        if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails))
        {
            cachedTokens = TryGetInt(promptDetails, "cached_tokens") ?? 0;
        }

        var reasoningTokens = 0;
        if (usage.TryGetProperty("completion_tokens_details", out var completionDetails))
        {
            reasoningTokens = TryGetInt(completionDetails, "reasoning_tokens") ?? 0;
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            CachedInputTokens = cachedTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = reasoningTokens,
            TotalTokens = totalTokens,
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
