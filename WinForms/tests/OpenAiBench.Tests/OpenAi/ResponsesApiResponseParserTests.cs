using System.Text.Json;
using OpenAiBench.Core.Domain;
using OpenAiBench.Infrastructure.OpenAi;
using Xunit;

namespace OpenAiBench.Tests.OpenAi;

public class ResponsesApiResponseParserTests
{
    private const string SampleResponseJson = """
    {
      "id": "resp_123",
      "model": "gpt-4.1-2025-04-14",
      "status": "completed",
      "output": [
        {
          "type": "message",
          "role": "assistant",
          "content": [
            { "type": "output_text", "text": "Hello, " },
            { "type": "output_text", "text": "world!" }
          ]
        }
      ],
      "usage": {
        "input_tokens": 1000,
        "input_tokens_details": { "cached_tokens": 400 },
        "output_tokens": 200,
        "output_tokens_details": { "reasoning_tokens": 50 },
        "total_tokens": 1200
      }
    }
    """;

    [Fact]
    public void ApplyResponseJson_ExtractsMetadataAndConcatenatedOutputText()
    {
        var run = new ExecutionRun { Snapshot = TestHelpers.CreateSnapshot() };
        using var document = JsonDocument.Parse(SampleResponseJson);

        ResponsesApiResponseParser.ApplyResponseJson(run, document.RootElement);

        Assert.Equal("resp_123", run.ResponseId);
        Assert.Equal("gpt-4.1-2025-04-14", run.ActualModel);
        Assert.Equal("completed", run.FinishReason);
        Assert.Equal("Hello, world!", run.Output);
    }

    [Fact]
    public void ApplyResponseJson_MapsCachedAndReasoningTokenDetails()
    {
        var run = new ExecutionRun { Snapshot = TestHelpers.CreateSnapshot() };
        using var document = JsonDocument.Parse(SampleResponseJson);

        ResponsesApiResponseParser.ApplyResponseJson(run, document.RootElement);

        Assert.Equal(1000, run.Usage.InputTokens);
        Assert.Equal(400, run.Usage.CachedInputTokens);
        Assert.Equal(600, run.Usage.UncachedInputTokens);
        Assert.Equal(200, run.Usage.OutputTokens);
        Assert.Equal(50, run.Usage.ReasoningTokens);
        Assert.Equal(1200, run.Usage.TotalTokens);
    }

    [Fact]
    public void ApplyResponseJson_MissingUsageDetails_DefaultsCachedAndReasoningToZero()
    {
        var run = new ExecutionRun { Snapshot = TestHelpers.CreateSnapshot() };
        using var document = JsonDocument.Parse("""{ "id": "r1", "usage": { "input_tokens": 10, "output_tokens": 5, "total_tokens": 15 } }""");

        ResponsesApiResponseParser.ApplyResponseJson(run, document.RootElement);

        Assert.Equal(0, run.Usage.CachedInputTokens);
        Assert.Equal(0, run.Usage.ReasoningTokens);
    }
}
