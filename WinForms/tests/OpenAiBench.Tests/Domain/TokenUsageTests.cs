using OpenAiBench.Core.Domain;
using Xunit;

namespace OpenAiBench.Tests.Domain;

public class TokenUsageTests
{
    [Fact]
    public void CacheHitPercentage_ComputesRatioOfCachedToInput()
    {
        var usage = new TokenUsage { InputTokens = 1000, CachedInputTokens = 250 };

        Assert.Equal(25.0, usage.CacheHitPercentage);
    }

    [Fact]
    public void CacheHitPercentage_ZeroInputTokens_ReturnsZeroNotNaN()
    {
        var usage = new TokenUsage { InputTokens = 0, CachedInputTokens = 0 };

        Assert.Equal(0, usage.CacheHitPercentage);
    }

    [Fact]
    public void UncachedInputTokens_IsInputMinusCached()
    {
        var usage = new TokenUsage { InputTokens = 1000, CachedInputTokens = 300 };

        Assert.Equal(700, usage.UncachedInputTokens);
    }
}
