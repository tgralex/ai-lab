using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Statistics;
using Xunit;

namespace AiLab.Tests.Statistics;

public class CostCalculatorTests
{
    private static readonly ProviderModel Model = new()
    {
        ProviderId = "openai",
        ModelId = "gpt-test",
        DisplayName = "GPT Test",
        InputPricePerMillion = 10m,
        CachedInputPricePerMillion = 1m,
        OutputPricePerMillion = 50m,
    };

    [Fact]
    public void Calculate_SplitsCostAcrossUncachedCachedAndOutput()
    {
        var usage = new TokenUsage { InputTokens = 2_000_000, CachedInputTokens = 500_000, OutputTokens = 1_000_000 };

        var result = new CostCalculator().Calculate(usage, Model);

        // Uncached input = 1,500,000 tokens * $10/M = $15
        Assert.Equal(15m, result.InputCost);
        // Cached input = 500,000 tokens * $1/M = $0.5
        Assert.Equal(0.5m, result.CachedInputCost);
        // Output = 1,000,000 tokens * $50/M = $50
        Assert.Equal(50m, result.OutputCost);
        Assert.Equal(65.5m, result.TotalCost);
    }

    [Fact]
    public void Calculate_ZeroTokens_ZeroCost()
    {
        var result = new CostCalculator().Calculate(new TokenUsage(), Model);

        Assert.Equal(0m, result.TotalCost);
    }

    [Fact]
    public void Calculate_MissingPricing_TreatsAsZero()
    {
        var modelWithNoPricing = new ProviderModel { ProviderId = "openai", ModelId = "unknown", DisplayName = "Unknown" };
        var usage = new TokenUsage { InputTokens = 1000, OutputTokens = 1000 };

        var result = new CostCalculator().Calculate(usage, modelWithNoPricing);

        Assert.Equal(0m, result.TotalCost);
    }
}
