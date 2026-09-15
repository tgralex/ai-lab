using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Grok;

/// <summary>
/// Reassembles OpenAI-Chat-Completions-style streaming chunks (`choices[0].delta.content`, with a
/// final chunk carrying `usage` via the request's `stream_options.include_usage`) into one logical
/// result — mirrors AnthropicStreamAccumulator's role for Anthropic's differently-shaped stream.
/// </summary>
internal sealed class GrokStreamAccumulator
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

        _responseId ??= TryGetString(root, "id");
        _actualModel ??= TryGetString(root, "model");

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            _finishReason = TryGetString(choice, "finish_reason") ?? _finishReason;

            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    _outputText.Append(text);
                    LastDelta = text;
                }
            }
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _inputTokens = TryGetInt(usage, "prompt_tokens") ?? _inputTokens;
            _outputTokens = TryGetInt(usage, "completion_tokens") ?? _outputTokens;

            if (usage.TryGetProperty("prompt_tokens_details", out var promptDetails))
            {
                _cachedInputTokens = TryGetInt(promptDetails, "cached_tokens") ?? _cachedInputTokens;
            }

            if (usage.TryGetProperty("completion_tokens_details", out var completionDetails))
            {
                _reasoningTokens = TryGetInt(completionDetails, "reasoning_tokens") ?? _reasoningTokens;
            }
        }
    }

    public ParsedGrokResponse ToParsedResponse() => new()
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
            ["id"] = _responseId,
            ["model"] = _actualModel,
            ["finish_reason"] = _finishReason,
            ["output_text"] = _outputText.ToString(),
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = _inputTokens,
                ["completion_tokens"] = _outputTokens,
                ["total_tokens"] = _inputTokens + _outputTokens,
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
