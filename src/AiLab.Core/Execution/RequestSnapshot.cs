namespace AiLab.Core.Execution;

/// <summary>
/// Immutable capture of the fully-resolved request configuration as it was at execution time,
/// so a run stays reproducible even if the owning AiRequestDefinition is edited later.
/// </summary>
public sealed class RequestSnapshot
{
    public required string ProviderId { get; init; }

    public required string ModelId { get; init; }

    public string? SystemPrompt { get; init; }

    public string? ResolvedCachedContext { get; init; }

    public string? ResolvedUserContext { get; init; }

    public IReadOnlyDictionary<string, string> ResolvedBindings { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> AttachmentHashes { get; init; } = [];

    public string? ReasoningEffort { get; init; }

    public bool Streaming { get; init; }

    public int? MaxOutputTokens { get; init; }

    public double? Temperature { get; init; }

    public string? StructuredOutputSchema { get; init; }

    public string? PromptCacheKey { get; init; }

    public IReadOnlyDictionary<string, string> ProviderSettings { get; init; } = new Dictionary<string, string>();

    public DateTimeOffset CapturedAt { get; init; }
}
