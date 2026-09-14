namespace OpenAiBench.Core.Cost;

public sealed class CostResult
{
    public decimal InputCost { get; init; }
    public decimal CachedInputCost { get; init; }
    public decimal OutputCost { get; init; }
    public decimal TotalCost => InputCost + CachedInputCost + OutputCost;
}
