using System.Text.Json.Nodes;
using AiLab.Core.Providers;

namespace AiLab.Infrastructure.Grok;

/// <summary>
/// Builds an xAI Chat Completions (`POST /v1/chat/completions`) request body — xAI's API is
/// OpenAI-Chat-Completions-shaped, not the Responses API that OpenAiProvider targets, hence its
/// own request shape (messages array, max_tokens, response_format) rather than reusing OpenAiProvider.
/// </summary>
public static class GrokRequestBuilder
{
    public static JsonObject Build(AiRequestExecutionContext context)
    {
        var messages = new JsonArray();

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            messages.Add(BuildMessage("system", context.SystemPrompt));
        }

        if (!string.IsNullOrEmpty(context.CachedContextText))
        {
            messages.Add(BuildMessage("user", context.CachedContextText));
        }

        if (!string.IsNullOrEmpty(context.UserContextText))
        {
            messages.Add(BuildMessage("user", context.UserContextText));
        }

        var body = new JsonObject
        {
            ["model"] = context.ModelId,
            ["messages"] = messages,
            ["stream"] = context.Streaming,
        };

        if (context.Streaming)
        {
            // Without this, usage is omitted from the streamed chunks entirely (OpenAI-Chat-
            // Completions-compatible APIs only include it when explicitly asked for).
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (context.MaxOutputTokens is { } maxTokens)
        {
            body["max_tokens"] = maxTokens;
        }

        if (context.Temperature is { } temperature)
        {
            body["temperature"] = temperature;
        }

        if (context.StopSequences.Count > 0)
        {
            body["stop"] = new JsonArray(context.StopSequences.Select(s => (JsonNode)s).ToArray());
        }

        if (!string.IsNullOrEmpty(context.ReasoningEffort))
        {
            body["reasoning_effort"] = context.ReasoningEffort;
        }

        if (!string.IsNullOrEmpty(context.StructuredOutputSchema))
        {
            var schemaNode = JsonNode.Parse(context.StructuredOutputSchema);
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
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
