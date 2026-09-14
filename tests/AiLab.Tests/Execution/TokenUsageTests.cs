using AiLab.Core.Execution;
using Xunit;

namespace AiLab.Tests.Execution;

public class TokenUsageTests
{
    [Fact]
    public void UncachedInputTokens_IsInputMinusCached()
    {
        var usage = new TokenUsage { InputTokens = 1000, CachedInputTokens = 300 };

        Assert.Equal(700, usage.UncachedInputTokens);
    }

    [Fact]
    public void CacheHitPercentage_ZeroInputTokens_AvoidsNaN()
    {
        var usage = new TokenUsage { InputTokens = 0, CachedInputTokens = 0 };

        Assert.Equal(0, usage.CacheHitPercentage);
    }

    [Fact]
    public void CacheHitPercentage_ComputesCorrectly()
    {
        var usage = new TokenUsage { InputTokens = 1000, CachedInputTokens = 250 };

        Assert.Equal(25.0, usage.CacheHitPercentage);
    }
}

public class ExecutionRunTests
{
    [Fact]
    public void OutputTokensPerSecond_UsesGenerationDurationNotTotalDuration()
    {
        var started = DateTimeOffset.UtcNow;
        var run = new ExecutionRun
        {
            AiRequestId = Guid.NewGuid(),
            ProviderId = "openai",
            StartedAt = started,
            FirstOutputTokenAt = started.AddSeconds(1), // 1s TTFT
            FinishedAt = started.AddSeconds(3), // total 3s, but generation only 2s
            Usage = new TokenUsage { OutputTokens = 100 },
            Snapshot = new RequestSnapshot { ProviderId = "openai", ModelId = "gpt-test", CapturedAt = started },
        };

        // Generation duration = 3s - 1s = 2s -> 100 tokens / 2s = 50 tok/s (not 100/3)
        Assert.Equal(50, run.OutputTokensPerSecond!.Value, precision: 3);
    }

    [Fact]
    public void OutputTokensPerSecond_NullWithoutGenerationDuration()
    {
        var run = new ExecutionRun
        {
            AiRequestId = Guid.NewGuid(),
            ProviderId = "openai",
            Usage = new TokenUsage { OutputTokens = 100 },
            Snapshot = new RequestSnapshot { ProviderId = "openai", ModelId = "gpt-test", CapturedAt = DateTimeOffset.UtcNow },
        };

        Assert.Null(run.OutputTokensPerSecond);
    }
}
