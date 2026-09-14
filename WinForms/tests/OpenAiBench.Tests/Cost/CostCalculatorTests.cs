using OpenAiBench.Core.Cost;
using OpenAiBench.Core.Domain;
using Xunit;

namespace OpenAiBench.Tests.Cost;

public class CostCalculatorTests
{
    [Fact]
    public void Calculate_SplitsCostAcrossUncachedCachedAndOutputTokens()
    {
        var calculator = new CostCalculator();
        var usage = new TokenUsage { InputTokens = 1_000_000, CachedInputTokens = 400_000, OutputTokens = 500_000 };
        var pricing = new PricingEntry { ModelId = "test-model", InputPerMillion = 2m, CachedInputPerMillion = 0.5m, OutputPerMillion = 8m };

        var result = calculator.Calculate(usage, pricing);

        // 600k uncached * $2/M = $1.20 ; 400k cached * $0.5/M = $0.20 ; 500k output * $8/M = $4.00
        Assert.Equal(1.20m, result.InputCost);
        Assert.Equal(0.20m, result.CachedInputCost);
        Assert.Equal(4.00m, result.OutputCost);
        Assert.Equal(5.40m, result.TotalCost);
    }

    [Fact]
    public void Calculate_ZeroTokens_ProducesZeroCost()
    {
        var calculator = new CostCalculator();
        var pricing = new PricingEntry { ModelId = "test-model", InputPerMillion = 2m, CachedInputPerMillion = 0.5m, OutputPerMillion = 8m };

        var result = calculator.Calculate(TokenUsage.Empty, pricing);

        Assert.Equal(0m, result.TotalCost);
    }
}
