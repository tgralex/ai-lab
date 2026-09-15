using System.Text.Json;
using AiLab.Infrastructure.Gemini;
using Xunit;

namespace AiLab.Tests.Gemini;

public class GeminiResponseParserTests
{
    [Fact]
    public void Parse_ExtractsOutputTextFromFirstCandidate()
    {
        using var document = JsonDocument.Parse("""
            {
              "responseId": "resp-1",
              "modelVersion": "gemini-3.5-flash",
              "candidates": [{"content": {"parts": [{"text": "hello world"}]}, "finishReason": "STOP"}],
              "usageMetadata": {"promptTokenCount": 10, "candidatesTokenCount": 2, "totalTokenCount": 12}
            }
            """);

        var parsed = GeminiResponseParser.Parse(document.RootElement);

        Assert.Null(parsed.ErrorMessage);
        Assert.Equal("resp-1", parsed.ResponseId);
        Assert.Equal("gemini-3.5-flash", parsed.ActualModel);
        Assert.Equal("STOP", parsed.FinishReason);
        Assert.Equal("hello world", parsed.OutputText);
    }

    [Fact]
    public void Parse_ConcatenatesMultipleTextParts()
    {
        using var document = JsonDocument.Parse("""
            {
              "candidates": [{"content": {"parts": [{"text": "hello "}, {"text": "world"}]}, "finishReason": "STOP"}]
            }
            """);

        var parsed = GeminiResponseParser.Parse(document.RootElement);

        Assert.Equal("hello world", parsed.OutputText);
    }

    [Fact]
    public void Parse_ExtractsUsageIncludingCachedAndThoughtsTokens()
    {
        using var document = JsonDocument.Parse("""
            {
              "candidates": [{"content": {"parts": [{"text": "x"}]}, "finishReason": "STOP"}],
              "usageMetadata": {
                "promptTokenCount": 100,
                "candidatesTokenCount": 50,
                "totalTokenCount": 150,
                "cachedContentTokenCount": 20,
                "thoughtsTokenCount": 15
              }
            }
            """);

        var parsed = GeminiResponseParser.Parse(document.RootElement);

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

        var parsed = GeminiResponseParser.Parse(document.RootElement);

        Assert.Equal("invalid api key", parsed.ErrorMessage);
        Assert.Equal(string.Empty, parsed.OutputText);
    }

    [Fact]
    public void Parse_MissingCandidates_ReturnsEmptyOutputText()
    {
        using var document = JsonDocument.Parse("""
            { "responseId": "resp-1", "modelVersion": "gemini-3.5-flash" }
            """);

        var parsed = GeminiResponseParser.Parse(document.RootElement);

        Assert.Null(parsed.ErrorMessage);
        Assert.Equal(string.Empty, parsed.OutputText);
    }
}
