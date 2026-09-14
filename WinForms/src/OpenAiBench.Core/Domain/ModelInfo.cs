namespace OpenAiBench.Core.Domain;

/// <summary>
/// Model capability + pricing metadata, loaded from models.json. Pricing lives alongside capability
/// flags in one editable file (rather than a separate pricing file) since both change together
/// whenever OpenAI ships a new model — see ModelCatalog in Infrastructure.
/// </summary>
public sealed class ModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public bool Recommended { get; init; }
    public bool SupportsReasoningEffort { get; init; }
    public bool SupportsStructuredOutput { get; init; }
    public bool SupportsStreaming { get; init; } = true;
    public bool SupportsPromptCacheKey { get; init; }
    public int? MaxOutputTokensLimit { get; init; }

    public decimal InputPerMillion { get; init; }
    public decimal CachedInputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }

    public static ModelInfo Unknown(string id) => new()
    {
        Id = id,
        DisplayName = id,
        SupportsReasoningEffort = false,
        SupportsStructuredOutput = false,
        SupportsStreaming = true,
        SupportsPromptCacheKey = false,
        MaxOutputTokensLimit = null
    };
}
