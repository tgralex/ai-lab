using AiLab.Core.Providers;
using AiLab.Infrastructure.Grok;
using Xunit;

namespace AiLab.Tests.Grok;

public class GrokRequestBuilderTests
{
    private static AiRequestExecutionContext BuildContext(
        bool streaming = true, int? maxOutputTokens = null, string? reasoningEffort = null) => new()
    {
        ModelId = "grok-4",
        SystemPrompt = "You are helpful.",
        CachedContextText = "cached",
        UserContextText = "user",
        Attachments = [],
        Streaming = streaming,
        MaxOutputTokens = maxOutputTokens,
        ReasoningEffort = reasoningEffort,
        ProviderSettings = new Dictionary<string, string>(),
    };

    [Fact]
    public void Build_EmitsMessagesInSystemCachedUserOrder()
    {
        var body = GrokRequestBuilder.Build(BuildContext());

        var messages = body["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("You are helpful.", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("cached", messages[1]!["content"]!.GetValue<string>());
        Assert.Equal("user", messages[2]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Build_StreamingTrue_IncludesUsageInStreamOptions()
    {
        var body = GrokRequestBuilder.Build(BuildContext());

        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.True(body["stream_options"]!["include_usage"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_NonStreaming_OmitsStreamOptions()
    {
        var body = GrokRequestBuilder.Build(BuildContext(streaming: false));

        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Null(body["stream_options"]);
    }

    [Fact]
    public void Build_MaxOutputTokens_MapsToMaxTokens()
    {
        var body = GrokRequestBuilder.Build(BuildContext(maxOutputTokens: 512));

        Assert.Equal(512, body["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void Build_ReasoningEffort_IsPassedThrough()
    {
        var body = GrokRequestBuilder.Build(BuildContext(reasoningEffort: "high"));

        Assert.Equal("high", body["reasoning_effort"]!.GetValue<string>());
    }
}
