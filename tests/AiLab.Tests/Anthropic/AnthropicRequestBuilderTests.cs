using AiLab.Core.Providers;
using AiLab.Infrastructure.Anthropic;
using Xunit;

namespace AiLab.Tests.Anthropic;

public class AnthropicRequestBuilderTests
{
    private static AiRequestExecutionContext BuildContext(
        int? maxOutputTokens = null, int? modelMaxOutputTokens = null,
        string? reasoningEffort = null, string? structuredOutputSchema = null) => new()
    {
        ModelId = "claude-sonnet-4-5-20250929",
        CachedContextText = "cached",
        UserContextText = "user",
        Attachments = [],
        Streaming = true,
        MaxOutputTokens = maxOutputTokens,
        ModelMaxOutputTokens = modelMaxOutputTokens,
        ReasoningEffort = reasoningEffort,
        StructuredOutputSchema = structuredOutputSchema,
        ProviderSettings = new Dictionary<string, string>(),
    };

    private const string SimpleSchema = """{"type": "object", "properties": {"x": {"type": "string"}}}""";

    [Fact]
    public void Build_MaxOutputTokensSet_UsesRequestValueOverModelCeiling()
    {
        var body = AnthropicRequestBuilder.Build(BuildContext(maxOutputTokens: 2000, modelMaxOutputTokens: 64000));

        Assert.Equal(2000, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_MaxOutputTokensUnset_FallsBackToModelCeiling()
    {
        var body = AnthropicRequestBuilder.Build(BuildContext(modelMaxOutputTokens: 64000));

        Assert.Equal(64000, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_NeitherSet_FallsBackToHardcodedDefault()
    {
        var body = AnthropicRequestBuilder.Build(BuildContext());

        Assert.Equal(4096, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_StructuredOutputWithoutReasoning_ForcesToolChoice()
    {
        var body = AnthropicRequestBuilder.Build(BuildContext(structuredOutputSchema: SimpleSchema));

        Assert.Equal("tool", body["tool_choice"]!["type"]!.GetValue<string>());
        Assert.Equal("structured_output", body["tool_choice"]!["name"]!.GetValue<string>());
        Assert.Null(body["thinking"]);
    }

    [Fact]
    public void Build_StructuredOutputWithReasoning_UsesAutoToolChoiceInsteadOfForcing()
    {
        // Anthropic rejects a forced tool_choice when thinking is enabled ("Thinking may not be
        // enabled when tool_choice forces tool use") — this combination must not regress into a
        // 400 from the provider.
        var body = AnthropicRequestBuilder.Build(BuildContext(reasoningEffort: "high", structuredOutputSchema: SimpleSchema));

        Assert.Equal("auto", body["tool_choice"]!["type"]!.GetValue<string>());
        Assert.NotNull(body["thinking"]);
        Assert.Equal("enabled", body["thinking"]!["type"]!.GetValue<string>());
    }
}
