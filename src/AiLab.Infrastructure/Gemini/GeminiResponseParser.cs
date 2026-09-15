using System.Text;
using System.Text.Json;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Gemini;

public sealed class ParsedGeminiResponse
{
    public string? ResponseId { get; init; }

    public string? ActualModel { get; init; }

    public string? FinishReason { get; init; }

    public string OutputText { get; init; } = string.Empty;

    public TokenUsage Usage { get; init; } = new();

    public string? ErrorMessage { get; init; }
}

/// <summary>Parses a Gemini GenerateContentResponse object (non-streaming, or the accumulated state of a stream).</summary>
public static class GeminiResponseParser
{
    public static ParsedGeminiResponse Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : error.ToString();
            return new ParsedGeminiResponse { ErrorMessage = message ?? "Gemini returned an error." };
        }

        var responseId = TryGetString(root, "responseId");
        var actualModel = TryGetString(root, "modelVersion");
        string? finishReason = null;
        var outputText = string.Empty;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            finishReason = TryGetString(candidate, "finishReason");
            outputText = ExtractText(candidate);
        }

        return new ParsedGeminiResponse
        {
            ResponseId = responseId,
            ActualModel = actualModel,
            FinishReason = finishReason,
            OutputText = outputText,
            Usage = ExtractUsage(root),
        };
    }

    /// <summary>Concatenates every text part in the candidate's content — Gemini can split a single answer across multiple parts.</summary>
    public static string ExtractText(JsonElement candidate)
    {
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                builder.Append(textEl.GetString());
            }
        }

        return builder.ToString();
    }

    public static TokenUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new TokenUsage();
        }

        var inputTokens = TryGetInt(usage, "promptTokenCount") ?? 0;
        var outputTokens = TryGetInt(usage, "candidatesTokenCount") ?? 0;
        var totalTokens = TryGetInt(usage, "totalTokenCount") ?? (inputTokens + outputTokens);
        var cachedTokens = TryGetInt(usage, "cachedContentTokenCount") ?? 0;
        var reasoningTokens = TryGetInt(usage, "thoughtsTokenCount") ?? 0;

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
