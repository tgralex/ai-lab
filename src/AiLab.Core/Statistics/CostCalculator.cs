using AiLab.Core.Execution;
using AiLab.Core.Models;

namespace AiLab.Core.Statistics;

public sealed class CostResult
{
    public decimal InputCost { get; init; }

    public decimal CachedInputCost { get; init; }

    public decimal OutputCost { get; init; }

    public decimal TotalCost => InputCost + CachedInputCost + OutputCost;
}

public interface ICostCalculator
{
    CostResult Calculate(TokenUsage usage, ProviderModel model);
}

/// <summary>Splits cost into uncached-input / cached-input / output — ported from the WinForms CostCalculator.</summary>
public sealed class CostCalculator : ICostCalculator
{
    public CostResult Calculate(TokenUsage usage, ProviderModel model)
    {
        var inputCost = (model.InputPricePerMillion ?? 0) * usage.UncachedInputTokens / 1_000_000m;
        var cachedInputCost = (model.CachedInputPricePerMillion ?? 0) * usage.CachedInputTokens / 1_000_000m;
        var outputCost = (model.OutputPricePerMillion ?? 0) * usage.OutputTokens / 1_000_000m;

        return new CostResult
        {
            InputCost = inputCost,
            CachedInputCost = cachedInputCost,
            OutputCost = outputCost,
        };
    }
}
