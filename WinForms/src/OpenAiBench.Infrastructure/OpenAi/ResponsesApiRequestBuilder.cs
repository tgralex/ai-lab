using System.Text.Json.Nodes;
using OpenAiBench.Core.Domain;

namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>
/// Builds a POST /v1/responses request body. Cached-context content always precedes user-context
/// content in the "input" array, identically across runs, since OpenAI's prompt caching is automatic
/// prefix matching rather than a tagged field — that ordering discipline is what makes
/// <see cref="RequestDefinition.PromptCacheKey"/> meaningful.
/// </summary>
public static class ResponsesApiRequestBuilder
{
    public static JsonObject Build(OpenAiRequestPayload payload, ModelInfo modelInfo)
    {
        var input = new JsonArray();

        if (!string.IsNullOrEmpty(payload.ResolvedSystemPrompt))
        {
            input.Add(BuildMessage("system", payload.ResolvedSystemPrompt));
        }

        if (!string.IsNullOrEmpty(payload.ResolvedCachedContextText))
        {
            input.Add(BuildMessage("user", payload.ResolvedCachedContextText));
        }

        if (!string.IsNullOrEmpty(payload.ResolvedUserContextText))
        {
            input.Add(BuildMessage("user", payload.ResolvedUserContextText));
        }

        var root = new JsonObject
        {
            ["model"] = payload.Model,
            ["input"] = input,
            ["stream"] = payload.Stream
        };

        if (payload.MaxOutputTokens is { } maxTokens)
        {
            root["max_output_tokens"] = maxTokens;
        }

        if (!string.IsNullOrEmpty(payload.PromptCacheKey) && modelInfo.SupportsPromptCacheKey)
        {
            root["prompt_cache_key"] = payload.PromptCacheKey;
        }

        if (!string.IsNullOrEmpty(payload.ReasoningEffort) && modelInfo.SupportsReasoningEffort)
        {
            root["reasoning"] = new JsonObject { ["effort"] = payload.ReasoningEffort };
        }

        if (!string.IsNullOrEmpty(payload.ResponseSchema) && modelInfo.SupportsStructuredOutput)
        {
            // Let JsonException surface to the caller — an invalid schema should fail fast, before any network call.
            var schemaNode = JsonNode.Parse(payload.ResponseSchema);
            root["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "response",
                    ["schema"] = schemaNode,
                    ["strict"] = true
                }
            };
        }

        foreach (var (key, value) in payload.AdditionalSettings)
        {
            root[key] = value;
        }

        return root;
    }

    private static JsonObject BuildMessage(string role, string text) => new()
    {
        ["role"] = role,
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "input_text", ["text"] = text }
        }
    };
}
