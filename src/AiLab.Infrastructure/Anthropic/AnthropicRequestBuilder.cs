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
            // Anthropic requires max_tokens on every call, unlike OpenAI/Grok which can omit their
            // cap entirely — fall back to the model's own catalog max before an arbitrary default,
            // so a large multi-part task doesn't silently truncate just because the user didn't
            // set an explicit "Max Output Tokens" on the request.
            ["max_tokens"] = context.MaxOutputTokens ?? context.ModelMaxOutputTokens ?? 4096,
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

            // Anthropic rejects a forced tool_choice outright when thinking is enabled ("Thinking
            // may not be enabled when tool_choice forces tool use") — fall back to auto in that
            // case. With a single tool defined and the system prompt demanding its shape, the
            // model still calls it in practice; it's just no longer strictly guaranteed.
            body["tool_choice"] = string.IsNullOrEmpty(context.ReasoningEffort)
                ? new JsonObject { ["type"] = "tool", ["name"] = StructuredOutputToolName }
                : new JsonObject { ["type"] = "auto" };
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
