using System.Text.Json;
using AiLab.Core.Models;

namespace AiLab.Infrastructure.ModelCatalog;

/// <summary>Shape of the per-provider seed JSON files under ModelCatalog/Seed/ — hand-maintained since no provider API exposes pricing.</summary>
public sealed class ModelSeedEntry
{
    public required string ModelId { get; init; }

    public required string DisplayName { get; init; }

    public bool Recommended { get; init; }

    public bool IsDeprecated { get; init; }

    public int? ContextWindowTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool SupportsReasoning { get; init; }

    public List<string> SupportedReasoningLevels { get; init; } = [];

    public bool SupportsStreaming { get; init; } = true;

    public bool SupportsStructuredOutput { get; init; }

    public bool SupportsToolCalling { get; init; }

    public bool SupportsPromptCacheKey { get; init; }

    public List<string> InputModalities { get; init; } = ["text"];

    public List<string> OutputModalities { get; init; } = ["text"];

    public decimal? InputPerMillion { get; init; }

    public decimal? CachedInputPerMillion { get; init; }

    public decimal? OutputPerMillion { get; init; }

    public ProviderModel ToProviderModel(string providerId) => new()
    {
        ProviderId = providerId,
        ModelId = ModelId,
        DisplayName = DisplayName,
        IsActive = !IsDeprecated,
        IsDeprecated = IsDeprecated,
        Recommended = Recommended,
        ContextWindowTokens = ContextWindowTokens,
        MaxOutputTokens = MaxOutputTokens,
        SupportsReasoning = SupportsReasoning,
        SupportedReasoningLevels = SupportedReasoningLevels,
        SupportsStreaming = SupportsStreaming,
        SupportsStructuredOutput = SupportsStructuredOutput,
        SupportsToolCalling = SupportsToolCalling,
        SupportsPromptCacheKey = SupportsPromptCacheKey,
        InputModalities = InputModalities,
        OutputModalities = OutputModalities,
        InputPricePerMillion = InputPerMillion,
        CachedInputPricePerMillion = CachedInputPerMillion,
        OutputPricePerMillion = OutputPerMillion,
        Source = ModelRefreshSource.SeedOnly,
    };
}

public static class ModelSeedLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ProviderModel> Load(string providerId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ModelCatalog", "Seed", $"{providerId}.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<ModelSeedEntry>>(json, Options) ?? [];
        return entries.Select(e => e.ToProviderModel(providerId)).ToList();
    }
}
