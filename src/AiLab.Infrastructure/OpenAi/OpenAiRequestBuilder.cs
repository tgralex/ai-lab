using System.Text.Json.Nodes;
using AiLab.Core.Providers;

namespace AiLab.Infrastructure.OpenAi;

/// <summary>
/// Builds the OpenAI Responses API (`POST /v1/responses`) request body. System/cached/user content
/// are emitted as separate input messages in that order, deliberately, so a shared prefix (system +
/// cached context) can be prompt-cached by OpenAI across repeated calls.
/// </summary>
public static class OpenAiRequestBuilder
{
    public static JsonObject Build(AiRequestExecutionContext context)
    {
        // context.StopSequences is deliberately never sent here — unlike OpenAI's older Chat
        // Completions API, the Responses API has no `stop` parameter at all (confirmed against
        // OpenAI's own docs and community reports of a hard 400 "Unknown parameter: 'stop'" when
        // sent). Anthropic, Gemini, and Grok's builders all support it.
        var input = new JsonArray();

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            input.Add(BuildMessage("system", context.SystemPrompt));
        }

        if (!string.IsNullOrEmpty(context.CachedContextText))
        {
            input.Add(BuildMessage("user", context.CachedContextText));
        }

        if (!string.IsNullOrEmpty(context.UserContextText))
        {
            input.Add(BuildMessage("user", context.UserContextText));
        }

        var body = new JsonObject
        {
            ["model"] = context.ModelId,
            ["input"] = input,
            ["stream"] = context.Streaming,
        };

        if (context.MaxOutputTokens is { } maxTokens)
        {
            body["max_output_tokens"] = maxTokens;
        }

        if (!string.IsNullOrEmpty(context.PromptCacheKey))
        {
            body["prompt_cache_key"] = context.PromptCacheKey;
        }

        if (!string.IsNullOrEmpty(context.ReasoningEffort))
        {
            body["reasoning"] = new JsonObject { ["effort"] = context.ReasoningEffort };
        }
        else if (context.Temperature is { } temperature)
        {
            // OpenAI's reasoning models reject `temperature` outright, so it's only sent
            // alongside the absence of `reasoning` above (which only reasoning-capable models get).
            body["temperature"] = temperature;
        }

        if (!string.IsNullOrEmpty(context.StructuredOutputSchema))
        {
            var schemaNode = JsonNode.Parse(context.StructuredOutputSchema);
            body["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "structured_output",
                    ["schema"] = schemaNode,
                    ["strict"] = true,
                },
            };
        }

        foreach (var (key, value) in context.ProviderSettings)
        {
            body[key] = JsonNode.Parse(value) ?? JsonValue.Create(value);
        }

        return body;
    }

    private static JsonObject BuildMessage(string role, string text) => new()
    {
        ["role"] = role,
        ["content"] = text,
    };
}
