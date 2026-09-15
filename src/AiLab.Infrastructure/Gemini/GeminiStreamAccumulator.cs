using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Gemini;

/// <summary>
/// Reassembles Gemini's `streamGenerateContent?alt=sse` chunks into one logical result. Each SSE
/// chunk is a full partial GenerateContentResponse (same shape as the non-streaming response),
/// with `candidates[0].content.parts[].text` carrying just that chunk's incremental new text —
/// mirrors GrokStreamAccumulator's role for Grok's differently-shaped stream.
/// </summary>
internal sealed class GeminiStreamAccumulator
{
    private readonly StringBuilder _outputText = new();

    private string? _responseId;
    private string? _actualModel;
    private string? _finishReason;
    private int _inputTokens;
    private int _cachedInputTokens;
    private int _outputTokens;
    private int _reasoningTokens;

    /// <summary>The text delta applied by the most recent ApplyChunk call, if any — null otherwise.</summary>
    public string? LastDelta { get; private set; }

    public void ApplyChunk(JsonElement root)
    {
        LastDelta = null;

        _responseId ??= TryGetString(root, "responseId");
        _actualModel ??= TryGetString(root, "modelVersion");

        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            _finishReason = TryGetString(candidate, "finishReason") ?? _finishReason;

            var text = GeminiResponseParser.ExtractText(candidate);
            if (!string.IsNullOrEmpty(text))
            {
                _outputText.Append(text);
                LastDelta = text;
            }
        }

        if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _inputTokens = TryGetInt(usage, "promptTokenCount") ?? _inputTokens;
            _outputTokens = TryGetInt(usage, "candidatesTokenCount") ?? _outputTokens;
            _cachedInputTokens = TryGetInt(usage, "cachedContentTokenCount") ?? _cachedInputTokens;
            _reasoningTokens = TryGetInt(usage, "thoughtsTokenCount") ?? _reasoningTokens;
        }
    }

    public ParsedGeminiResponse ToParsedResponse() => new()
    {
        ResponseId = _responseId,
        ActualModel = _actualModel,
        FinishReason = _finishReason,
        OutputText = _outputText.ToString(),
        Usage = new TokenUsage
        {
            InputTokens = _inputTokens,
            CachedInputTokens = _cachedInputTokens,
            OutputTokens = _outputTokens,
            ReasoningTokens = _reasoningTokens,
            TotalTokens = _inputTokens + _outputTokens,
        },
    };

    public string ToRawJson()
    {
        var node = new JsonObject
        {
            ["responseId"] = _responseId,
            ["modelVersion"] = _actualModel,
            ["finishReason"] = _finishReason,
            ["output_text"] = _outputText.ToString(),
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = _inputTokens,
                ["candidatesTokenCount"] = _outputTokens,
                ["totalTokenCount"] = _inputTokens + _outputTokens,
            },
        };

        return node.ToJsonString();
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
