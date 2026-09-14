using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Ports;

namespace OpenAiBench.Infrastructure.Pricing;

/// <summary>
/// Loads models.json once and serves both model-capability lookups and per-model pricing from it —
/// one editable file to update whenever OpenAI ships a new model or changes prices, instead of two
/// files that can drift out of sync. <see cref="Core.Cost.CostCalculator"/> only ever sees a
/// <see cref="PricingEntry"/> through <see cref="IPricingProvider"/>, so the merge is invisible to it.
/// </summary>
public sealed class ModelCatalog : IModelCapabilityProvider, IPricingProvider
{
    private readonly List<ModelInfo> _modelsInOrder;
    private readonly Dictionary<string, ModelInfo> _byId;

    public ModelCatalog(string modelsFilePath)
    {
        _modelsInOrder = new List<ModelInfo>();
        _byId = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(modelsFilePath))
        {
            return;
        }

        var json = File.ReadAllText(modelsFilePath);
        var entries = JsonSerializer.Deserialize<List<ModelEntryDto>>(json, JsonOptions) ?? new List<ModelEntryDto>();

        foreach (var entry in entries)
        {
            var model = new ModelInfo
            {
                Id = entry.Id,
                DisplayName = entry.DisplayName,
                Recommended = entry.Recommended,
                SupportsReasoningEffort = entry.SupportsReasoningEffort,
                SupportsStructuredOutput = entry.SupportsStructuredOutput,
                SupportsStreaming = entry.SupportsStreaming,
                SupportsPromptCacheKey = entry.SupportsPromptCacheKey,
                MaxOutputTokensLimit = entry.MaxOutputTokensLimit,
                InputPerMillion = entry.InputPerMillion,
                CachedInputPerMillion = entry.CachedInputPerMillion,
                OutputPerMillion = entry.OutputPerMillion
            };

            _modelsInOrder.Add(model);
            _byId[entry.Id] = model;
        }
    }

    public IReadOnlyList<ModelInfo> GetAll() => _modelsInOrder;

    public ModelInfo Get(string modelId) =>
        _byId.TryGetValue(modelId, out var model) ? model : ModelInfo.Unknown(modelId);

    public PricingEntry? TryGet(string modelId) =>
        _byId.TryGetValue(modelId, out var model)
            ? new PricingEntry
            {
                ModelId = model.Id,
                InputPerMillion = model.InputPerMillion,
                CachedInputPerMillion = model.CachedInputPerMillion,
                OutputPerMillion = model.OutputPerMillion
            }
            : null;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class ModelEntryDto
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
        [JsonPropertyName("recommended")] public bool Recommended { get; init; }
        [JsonPropertyName("supportsReasoningEffort")] public bool SupportsReasoningEffort { get; init; }
        [JsonPropertyName("supportsStructuredOutput")] public bool SupportsStructuredOutput { get; init; }
        [JsonPropertyName("supportsStreaming")] public bool SupportsStreaming { get; init; } = true;
        [JsonPropertyName("supportsPromptCacheKey")] public bool SupportsPromptCacheKey { get; init; }
        [JsonPropertyName("maxOutputTokensLimit")] public int? MaxOutputTokensLimit { get; init; }
        [JsonPropertyName("inputPerMillion")] public decimal InputPerMillion { get; init; }
        [JsonPropertyName("cachedInputPerMillion")] public decimal CachedInputPerMillion { get; init; }
        [JsonPropertyName("outputPerMillion")] public decimal OutputPerMillion { get; init; }
    }
}
