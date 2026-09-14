using OpenAiBench.Core.Domain;
using Xunit;

namespace OpenAiBench.Tests.Domain;

public class ExecutionRunTests
{
    [Fact]
    public void OutputTokensPerSecond_UsesGenerationDurationNotTotalDuration()
    {
        var run = TestHelpers.CreateRun(
            totalDuration: TimeSpan.FromSeconds(10),
            generationDuration: TimeSpan.FromSeconds(2),
            usage: new TokenUsage { OutputTokens = 100 });

        Assert.Equal(50, run.OutputTokensPerSecond);
    }

    [Fact]
    public void OutputTokensPerSecond_NoGenerationDuration_IsNull()
    {
        var run = TestHelpers.CreateRun(generationDuration: null);

        Assert.Null(run.OutputTokensPerSecond);
    }

    [Fact]
    public void PromptCacheUsed_ReflectsWhetherAnyTokensWereCached()
    {
        var withCache = TestHelpers.CreateRun(usage: new TokenUsage { InputTokens = 100, CachedInputTokens = 50 });
        var withoutCache = TestHelpers.CreateRun(usage: new TokenUsage { InputTokens = 100, CachedInputTokens = 0 });

        Assert.True(withCache.PromptCacheUsed);
        Assert.False(withoutCache.PromptCacheUsed);
    }
}
