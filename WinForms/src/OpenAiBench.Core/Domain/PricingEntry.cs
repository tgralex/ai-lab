namespace OpenAiBench.Core.Domain;

public sealed class PricingEntry
{
    public required string ModelId { get; init; }
    public decimal InputPerMillion { get; init; }
    public decimal CachedInputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
