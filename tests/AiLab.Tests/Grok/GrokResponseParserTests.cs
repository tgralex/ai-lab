using System.Text.Json;
using AiLab.Infrastructure.Grok;
using Xunit;

namespace AiLab.Tests.Grok;

public class GrokResponseParserTests
{
    [Fact]
    public void Parse_ExtractsOutputTextFromFirstChoice()
    {
        using var document = JsonDocument.Parse("""
            {
              "id": "resp-1",
              "model": "grok-4",
              "choices": [{"index": 0, "message": {"role": "assistant", "content": "hello world"}, "finish_reason": "stop"}],
              "usage": {"prompt_tokens": 10, "completion_tokens": 2, "total_tokens": 12}
            }
            """);

        var parsed = GrokResponseParser.Parse(document.RootElement);

        Assert.Null(parsed.ErrorMessage);
        Assert.Equal("resp-1", parsed.ResponseId);
        Assert.Equal("grok-4", parsed.ActualModel);
        Assert.Equal("stop", parsed.FinishReason);
        Assert.Equal("hello world", parsed.OutputText);
    }

    [Fact]
    public void Parse_ExtractsUsageIncludingCachedAndReasoningTokens()
    {
        using var document = JsonDocument.Parse("""
            {
              "id": "resp-1",
              "model": "grok-4",
              "choices": [{"index": 0, "message": {"role": "assistant", "content": "x"}, "finish_reason": "stop"}],
              "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 50,
                "total_tokens": 150,
                "prompt_tokens_details": {"cached_tokens": 20},
                "completion_tokens_details": {"reasoning_tokens": 15}
              }
            }
            """);

        var parsed = GrokResponseParser.Parse(document.RootElement);

        Assert.Equal(100, parsed.Usage.InputTokens);
        Assert.Equal(20, parsed.Usage.CachedInputTokens);
        Assert.Equal(50, parsed.Usage.OutputTokens);
        Assert.Equal(15, parsed.Usage.ReasoningTokens);
        Assert.Equal(150, parsed.Usage.TotalTokens);
    }

    [Fact]
    public void Parse_ErrorResponse_ReturnsErrorMessage()
    {
        using var document = JsonDocument.Parse("""
            { "error": { "message": "invalid api key" } }
            """);

        var parsed = GrokResponseParser.Parse(document.RootElement);

        Assert.Equal("invalid api key", parsed.ErrorMessage);
        Assert.Equal(string.Empty, parsed.OutputText);
    }

    [Fact]
    public void Parse_MissingChoices_ReturnsEmptyOutputText()
    {
        using var document = JsonDocument.Parse("""
            { "id": "resp-1", "model": "grok-4" }
            """);

        var parsed = GrokResponseParser.Parse(document.RootElement);

        Assert.Null(parsed.ErrorMessage);
        Assert.Equal(string.Empty, parsed.OutputText);
    }
}
