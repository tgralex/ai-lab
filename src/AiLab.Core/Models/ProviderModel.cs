namespace AiLab.Core.Models;

public enum ModelRefreshSource
{
    Live,
    SeedOnly,
    Mixed,
}

/// <summary>Provider-declared catalog metadata for one model — capabilities and pricing, not observed performance.</summary>
public sealed record ProviderModel
{
    public required string ProviderId { get; init; }

    public required string ModelId { get; init; }

    public required string DisplayName { get; init; }

    public bool IsActive { get; init; } = true;

    public bool IsDeprecated { get; init; }

    public bool Recommended { get; init; }

    public int? ContextWindowTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool SupportsReasoning { get; init; }

    public IReadOnlyList<string> SupportedReasoningLevels { get; init; } = [];

    public bool SupportsStreaming { get; init; } = true;

    public bool SupportsStructuredOutput { get; init; }

    public bool SupportsToolCalling { get; init; }

    public bool SupportsPromptCacheKey { get; init; }

    public IReadOnlyList<string> InputModalities { get; init; } = ["text"];

    public IReadOnlyList<string> OutputModalities { get; init; } = ["text"];

    public decimal? InputPricePerMillion { get; init; }

    public decimal? CachedInputPricePerMillion { get; init; }

    public decimal? OutputPricePerMillion { get; init; }

    public ModelRefreshSource Source { get; init; } = ModelRefreshSource.SeedOnly;

    public static ProviderModel Unknown(string providerId, string modelId) => new()
    {
        ProviderId = providerId,
        ModelId = modelId,
        DisplayName = modelId,
        IsActive = true,
        SupportsStreaming = true,
        Source = ModelRefreshSource.Live,
    };
}
