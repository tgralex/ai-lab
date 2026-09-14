using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Cost;

public sealed class CostCalculator : ICostCalculator
{
    private const decimal Million = 1_000_000m;

    public CostResult Calculate(TokenUsage usage, PricingEntry pricing)
    {
        var uncachedInput = (decimal)usage.UncachedInputTokens;
        var cachedInput = (decimal)usage.CachedInputTokens;
        var output = (decimal)usage.OutputTokens;

        return new CostResult
        {
            InputCost = uncachedInput * pricing.InputPerMillion / Million,
            CachedInputCost = cachedInput * pricing.CachedInputPerMillion / Million,
            OutputCost = output * pricing.OutputPerMillion / Million
        };
    }
}
