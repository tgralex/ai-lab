using System.Text.Json;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.OpenAi;

public sealed class ParsedOpenAiResponse
{
    public string? ResponseId { get; init; }

    public string? ActualModel { get; init; }

    public string? Status { get; init; }

    public string OutputText { get; init; } = string.Empty;

    public TokenUsage Usage { get; init; } = new();

    public string? ErrorMessage { get; init; }
}

/// <summary>Parses an OpenAI Responses API response object — ported from the WinForms ResponsesApiResponseParser.</summary>
public static class OpenAiResponseParser
{
    public static ParsedOpenAiResponse Parse(JsonElement root)
    {
        var responseId = TryGetString(root, "id");
        var actualModel = TryGetString(root, "model");
        var status = TryGetString(root, "status");
        var errorMessage = TryGetErrorMessage(root);

        var outputText = ExtractOutputText(root);
        var usage = ExtractUsage(root);

        return new ParsedOpenAiResponse
        {
            ResponseId = responseId,
            ActualModel = actualModel,
            Status = status,
            OutputText = outputText,
            Usage = usage,
            ErrorMessage = errorMessage,
        };
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (TryGetString(part, "type") == "output_text" && part.TryGetProperty("text", out var textElement))
                {
                    builder.Append(textElement.GetString());
                }
            }
        }

        return builder.ToString();
    }

    private static TokenUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return new TokenUsage();
        }

        var inputTokens = TryGetInt(usage, "input_tokens") ?? 0;
        var outputTokens = TryGetInt(usage, "output_tokens") ?? 0;
        var totalTokens = TryGetInt(usage, "total_tokens") ?? (inputTokens + outputTokens);

        var cachedTokens = 0;
        if (usage.TryGetProperty("input_tokens_details", out var inputDetails))
        {
            cachedTokens = TryGetInt(inputDetails, "cached_tokens") ?? 0;
        }

        var reasoningTokens = 0;
        if (usage.TryGetProperty("output_tokens_details", out var outputDetails))
        {
            reasoningTokens = TryGetInt(outputDetails, "reasoning_tokens") ?? 0;
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

    private static string? TryGetErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            return TryGetString(error, "message") ?? error.ToString();
        }

        return null;
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
