using System.Text.Json.Nodes;
using AiLab.Core.Providers;

namespace AiLab.Infrastructure.Anthropic;

/// <summary>
/// Builds an Anthropic Messages API (`POST /v1/messages`) request body. The cached-context block
/// gets `cache_control: {"type": "ephemeral"}` so Anthropic actually prompt-caches it — this is
/// what lets the app answer "does prompt caching help?" for Anthropic models.
/// </summary>
public static class AnthropicRequestBuilder
{
    private const string StructuredOutputToolName = "structured_output";

    public static JsonObject Build(AiRequestExecutionContext context)
    {
        var contentBlocks = new JsonArray();

        if (!string.IsNullOrEmpty(context.CachedContextText))
        {
            contentBlocks.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = context.CachedContextText,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            });
        }

        if (!string.IsNullOrEmpty(context.UserContextText))
        {
            contentBlocks.Add(new JsonObject { ["type"] = "text", ["text"] = context.UserContextText });
        }

        if (contentBlocks.Count == 0)
        {
            contentBlocks.Add(new JsonObject { ["type"] = "text", ["text"] = string.Empty });
        }

        var body = new JsonObject
        {
            ["model"] = context.ModelId,
            ["max_tokens"] = context.MaxOutputTokens ?? 4096, // Anthropic requires max_tokens on every call
            ["stream"] = context.Streaming,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = contentBlocks },
            },
        };

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            body["system"] = context.SystemPrompt;
        }

        if (!string.IsNullOrEmpty(context.ReasoningEffort))
        {
            body["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = MapReasoningEffortToThinkingBudget(context.ReasoningEffort),
            };
        }

        if (!string.IsNullOrEmpty(context.StructuredOutputSchema))
        {
            var schemaNode = JsonNode.Parse(context.StructuredOutputSchema);
            body["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = StructuredOutputToolName,
                    ["description"] = "Return the structured output matching the required schema.",
                    ["input_schema"] = schemaNode,
                },
            };
            body["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = StructuredOutputToolName };
        }

        foreach (var (key, value) in context.ProviderSettings)
        {
            body[key] = JsonNode.Parse(value) ?? JsonValue.Create(value);
        }

        return body;
    }

    /// <summary>
    /// Anthropic has no discrete "low/medium/high" reasoning-effort setting — extended thinking is
    /// controlled by a token budget. This is a pragmatic approximation so the same ReasoningConfig
    /// on an AiRequestDefinition means something for either provider.
    /// </summary>
    private static int MapReasoningEffortToThinkingBudget(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => 1024,
        "medium" => 4096,
        "high" => 16000,
        _ => 4096,
    };
}
