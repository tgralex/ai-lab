using System.Text.Json.Nodes;
using AiLab.Core.Providers;

namespace AiLab.Infrastructure.Gemini;

/// <summary>
/// Builds a Gemini `generateContent`/`streamGenerateContent` request body. Unlike OpenAI/Grok,
/// the system prompt is a separate top-level `systemInstruction` field, not an inline message —
/// closer to Anthropic's shape in that respect. Reasoning ("thinking") and structured output are
/// both independent `generationConfig` parameters with no interaction between them (unlike
/// Anthropic, where structured output is a forced tool call that's incompatible with thinking).
/// </summary>
public static class GeminiRequestBuilder
{
    public static JsonObject Build(AiRequestExecutionContext context)
    {
        var contents = new JsonArray();

        if (!string.IsNullOrEmpty(context.CachedContextText))
        {
            contents.Add(BuildUserContent(context.CachedContextText));
        }

        if (!string.IsNullOrEmpty(context.UserContextText))
        {
            contents.Add(BuildUserContent(context.UserContextText));
        }

        if (contents.Count == 0)
        {
            // A request driven entirely by its System Prompt (no cached/user context) would
            // otherwise send an empty `contents` part here — Gemini, like Anthropic, requires the
            // turn to carry non-empty/non-whitespace text (see AnthropicRequestBuilder for the
            // equivalent 400 this avoids). A single period satisfies that without adding any real
            // instruction.
            contents.Add(BuildUserContent("."));
        }

        var body = new JsonObject { ["contents"] = contents };

        if (!string.IsNullOrEmpty(context.SystemPrompt))
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = context.SystemPrompt } },
            };
        }

        var generationConfig = new JsonObject();

        // Optional, like OpenAI/Grok — Gemini has no equivalent of Anthropic's "max_tokens is
        // mandatory on every call" requirement, so omitting it lets the model use its own default.
        if (context.MaxOutputTokens is { } maxTokens)
        {
            generationConfig["maxOutputTokens"] = maxTokens;
        }
        else if (context.ModelMaxOutputTokens is { } modelMaxTokens)
        {
            generationConfig["maxOutputTokens"] = modelMaxTokens;
        }

        if (context.Temperature is { } temperature)
        {
            generationConfig["temperature"] = temperature;
        }

        if (context.StopSequences.Count > 0)
        {
            generationConfig["stopSequences"] = new JsonArray(context.StopSequences.Select(s => (JsonNode)s).ToArray());
        }

        if (!string.IsNullOrEmpty(context.ReasoningEffort))
        {
            // Direct passthrough — Gemini's thinkingLevel accepts "low"/"medium"/"high" natively,
            // the same vocabulary this app already uses, unlike Anthropic's effort-to-token-budget
            // mapping.
            generationConfig["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = context.ReasoningEffort };
        }

        if (!string.IsNullOrEmpty(context.StructuredOutputSchema))
        {
            generationConfig["responseMimeType"] = "application/json";
            // Gemini's responseSchema is a restricted subset of JSON Schema, not the real thing —
            // see GeminiSchemaConverter for exactly what it rejects and why.
            generationConfig["responseSchema"] = GeminiSchemaConverter.Convert(JsonNode.Parse(context.StructuredOutputSchema));
        }

        if (generationConfig.Count > 0)
        {
            body["generationConfig"] = generationConfig;
        }

        foreach (var (key, value) in context.ProviderSettings)
        {
            body[key] = JsonNode.Parse(value) ?? JsonValue.Create(value);
        }

        return body;
    }

    private static JsonObject BuildUserContent(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } },
    };
}
