using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Cost;

public interface ICostCalculator
{
    CostResult Calculate(TokenUsage usage, PricingEntry pricing);
}
