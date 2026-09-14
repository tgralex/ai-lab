namespace OpenAiBench.Core.Domain;

/// <summary>
/// Fully-resolved request handed to <see cref="Ports.IOpenAiExperimentClient"/> — variables substituted,
/// file text already inlined into the context strings, ready to be turned into an API request body.
/// </summary>
public sealed class OpenAiRequestPayload
{
    public required string Model { get; init; }
    public required string ResolvedSystemPrompt { get; init; }
    public required string ResolvedCachedContextText { get; init; }
    public required string ResolvedUserContextText { get; init; }
    public string? ReasoningEffort { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool Stream { get; init; }
    public string? PromptCacheKey { get; init; }
    public string? ResponseSchema { get; init; }
    public Dictionary<string, string> AdditionalSettings { get; init; } = new();
    public required RequestSnapshot Snapshot { get; init; }
}

public sealed class StreamingUpdate
{
    public ExecutionStatus Status { get; init; }
    public string? DeltaText { get; init; }
}
