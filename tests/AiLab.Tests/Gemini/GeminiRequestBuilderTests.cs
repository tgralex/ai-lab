using AiLab.Core.Providers;
using AiLab.Infrastructure.Gemini;
using Xunit;

namespace AiLab.Tests.Gemini;

public class GeminiRequestBuilderTests
{
    private static AiRequestExecutionContext BuildContext(
        bool streaming = true, int? maxOutputTokens = null, int? modelMaxOutputTokens = null,
        string? reasoningEffort = null, string? structuredOutputSchema = null) => new()
    {
        ModelId = "gemini-3.5-flash",
        SystemPrompt = "You are helpful.",
        CachedContextText = "cached",
        UserContextText = "user",
        Attachments = [],
        Streaming = streaming,
        MaxOutputTokens = maxOutputTokens,
        ModelMaxOutputTokens = modelMaxOutputTokens,
        ReasoningEffort = reasoningEffort,
        StructuredOutputSchema = structuredOutputSchema,
        ProviderSettings = new Dictionary<string, string>(),
    };

    [Fact]
    public void Build_EmitsCachedThenUserContentInOrder()
    {
        var body = GeminiRequestBuilder.Build(BuildContext());

        var contents = body["contents"]!.AsArray();
        Assert.Equal(2, contents.Count);
        Assert.Equal("user", contents[0]!["role"]!.GetValue<string>());
        Assert.Equal("cached", contents[0]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("user", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("user", contents[1]!["parts"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Build_SystemPrompt_GoesToTopLevelSystemInstructionNotContents()
    {
        var body = GeminiRequestBuilder.Build(BuildContext());

        Assert.Equal("You are helpful.", body["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        // Only cached + user text land in `contents` — never the system prompt.
        Assert.Equal(2, body["contents"]!.AsArray().Count);
    }

    [Fact]
    public void Build_MaxOutputTokensSet_MapsToGenerationConfig()
    {
        var body = GeminiRequestBuilder.Build(BuildContext(maxOutputTokens: 512));

        Assert.Equal(512, body["generationConfig"]!["maxOutputTokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_MaxOutputTokensUnset_OmitsGenerationConfigField()
    {
        // Optional, like OpenAI/Grok — Gemini has no "must send max_tokens on every call"
        // requirement the way Anthropic does.
        var body = GeminiRequestBuilder.Build(BuildContext());

        Assert.Null(body["generationConfig"]?["maxOutputTokens"]);
    }

    [Fact]
    public void Build_MaxOutputTokensUnset_FallsBackToModelCeiling()
    {
        var body = GeminiRequestBuilder.Build(BuildContext(modelMaxOutputTokens: 65536));

        Assert.Equal(65536, body["generationConfig"]!["maxOutputTokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_ReasoningEffort_MapsDirectlyToThinkingLevel()
    {
        // Direct passthrough — no effort-to-token-budget mapping needed, unlike Anthropic.
        var body = GeminiRequestBuilder.Build(BuildContext(reasoningEffort: "high"));

        Assert.Equal("high", body["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    [Fact]
    public void Build_StructuredOutputSchema_SetsResponseMimeTypeAndSchema()
    {
        const string schema = """{"type": "object", "properties": {"x": {"type": "string"}}}""";
        var body = GeminiRequestBuilder.Build(BuildContext(structuredOutputSchema: schema));

        Assert.Equal("application/json", body["generationConfig"]!["responseMimeType"]!.GetValue<string>());
        Assert.Equal("object", body["generationConfig"]!["responseSchema"]!["type"]!.GetValue<string>());
    }
}
