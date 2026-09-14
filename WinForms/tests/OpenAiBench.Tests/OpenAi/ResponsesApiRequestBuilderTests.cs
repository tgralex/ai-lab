using OpenAiBench.Core.Domain;
using OpenAiBench.Infrastructure.OpenAi;
using Xunit;

namespace OpenAiBench.Tests.OpenAi;

public class ResponsesApiRequestBuilderTests
{
    private static OpenAiRequestPayload CreatePayload(Action<OpenAiRequestPayloadBuilder>? configure = null)
    {
        var builder = new OpenAiRequestPayloadBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public void Build_CachedContextPrecedesUserContextInInputArray()
    {
        var payload = CreatePayload(b =>
        {
            b.CachedContextText = "CACHED";
            b.UserContextText = "USER";
        });
        var model = new ModelInfo { Id = "gpt-4.1", DisplayName = "gpt-4.1" };

        var request = ResponsesApiRequestBuilder.Build(payload, model);
        var input = request["input"]!.AsArray();

        var cachedIndex = IndexOfMessageContaining(input, "CACHED");
        var userIndex = IndexOfMessageContaining(input, "USER");

        Assert.True(cachedIndex < userIndex);
    }

    [Fact]
    public void Build_ModelDoesNotSupportReasoning_OmitsReasoningField()
    {
        var payload = CreatePayload(b => b.ReasoningEffort = "high");
        var model = new ModelInfo { Id = "gpt-4.1", DisplayName = "gpt-4.1", SupportsReasoningEffort = false };

        var request = ResponsesApiRequestBuilder.Build(payload, model);

        Assert.Null(request["reasoning"]);
    }

    [Fact]
    public void Build_ModelSupportsReasoning_IncludesReasoningEffort()
    {
        var payload = CreatePayload(b => b.ReasoningEffort = "high");
        var model = new ModelInfo { Id = "gpt-5", DisplayName = "gpt-5", SupportsReasoningEffort = true };

        var request = ResponsesApiRequestBuilder.Build(payload, model);

        Assert.Equal("high", request["reasoning"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public void Build_NoResponseSchema_OmitsTextFormatField()
    {
        var payload = CreatePayload();
        var model = new ModelInfo { Id = "gpt-4.1", DisplayName = "gpt-4.1", SupportsStructuredOutput = true };

        var request = ResponsesApiRequestBuilder.Build(payload, model);

        Assert.Null(request["text"]);
    }

    [Fact]
    public void Build_WithResponseSchema_IncludesJsonSchemaFormat()
    {
        var payload = CreatePayload(b => b.ResponseSchema = """{"type":"object"}""");
        var model = new ModelInfo { Id = "gpt-4.1", DisplayName = "gpt-4.1", SupportsStructuredOutput = true };

        var request = ResponsesApiRequestBuilder.Build(payload, model);

        Assert.Equal("json_schema", request["text"]!["format"]!["type"]!.GetValue<string>());
    }

    private static int IndexOfMessageContaining(System.Text.Json.Nodes.JsonArray input, string text)
    {
        for (var i = 0; i < input.Count; i++)
        {
            if (input[i]!.ToJsonString().Contains(text))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class OpenAiRequestPayloadBuilder
    {
        public string CachedContextText { get; set; } = string.Empty;
        public string UserContextText { get; set; } = string.Empty;
        public string? ReasoningEffort { get; set; }
        public string? ResponseSchema { get; set; }

        public OpenAiRequestPayload Build() => new()
        {
            Model = "gpt-4.1",
            ResolvedSystemPrompt = string.Empty,
            ResolvedCachedContextText = CachedContextText,
            ResolvedUserContextText = UserContextText,
            ReasoningEffort = ReasoningEffort,
            ResponseSchema = ResponseSchema,
            Stream = true,
            Snapshot = TestHelpers.CreateSnapshot()
        };
    }
}
