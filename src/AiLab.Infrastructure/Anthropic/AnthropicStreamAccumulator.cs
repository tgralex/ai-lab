using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiLab.Core.Execution;

namespace AiLab.Infrastructure.Anthropic;

/// <summary>
/// Reassembles the pieces Anthropic spreads across message_start / content_block_delta /
/// message_delta events into one logical result, since (unlike OpenAI's response.completed)
/// Anthropic never sends a single final full-message event over the stream.
/// </summary>
internal sealed class AnthropicStreamAccumulator
{
    private readonly StringBuilder _outputText = new();

    private string? _responseId;
    private string? _actualModel;
    private string? _stopReason;
    private int _inputTokens;
    private int _cachedInputTokens;
    private int _outputTokens;

    public string? ErrorMessage { get; set; }

    public void ApplyMessageStart(JsonElement message)
    {
        _responseId = TryGetString(message, "id");
        _actualModel = TryGetString(message, "model");

        if (message.TryGetProperty("usage", out var usage))
        {
            _inputTokens = TryGetInt(usage, "input_tokens") ?? 0;
            _cachedInputTokens = TryGetInt(usage, "cache_read_input_tokens") ?? 0;
        }
    }

    public void AppendOutputText(string delta) => _outputText.Append(delta);

    public void ApplyMessageDelta(JsonElement root)
    {
        if (root.TryGetProperty("delta", out var delta))
        {
            _stopReason = TryGetString(delta, "stop_reason") ?? _stopReason;
        }

        if (root.TryGetProperty("usage", out var usage))
        {
            _outputTokens = TryGetInt(usage, "output_tokens") ?? _outputTokens;
        }
    }

    public ParsedAnthropicResponse ToParsedResponse() => new()
    {
        ResponseId = _responseId,
        ActualModel = _actualModel,
        StopReason = _stopReason,
        OutputText = _outputText.ToString(),
        Usage = new TokenUsage
        {
            InputTokens = _inputTokens + _cachedInputTokens,
            CachedInputTokens = _cachedInputTokens,
            OutputTokens = _outputTokens,
            ReasoningTokens = 0,
            TotalTokens = _inputTokens + _cachedInputTokens + _outputTokens,
        },
    };

    public string ToRawJson()
    {
        var node = new JsonObject
        {
            ["id"] = _responseId,
            ["model"] = _actualModel,
            ["stop_reason"] = _stopReason,
            ["output_text"] = _outputText.ToString(),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = _inputTokens,
                ["cache_read_input_tokens"] = _cachedInputTokens,
                ["output_tokens"] = _outputTokens,
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
