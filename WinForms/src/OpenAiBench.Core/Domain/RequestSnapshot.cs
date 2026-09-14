namespace OpenAiBench.Core.Domain;

/// <summary>
/// The exact request configuration used for one execution, captured at execution time so a run
/// stays reproducible even if the parent experiment is edited afterward.
/// </summary>
public sealed class RequestSnapshot
{
    public required RequestDefinition Request { get; init; }
    public required string ResolvedSystemPrompt { get; init; }
    public required string ResolvedCachedContextText { get; init; }
    public required string ResolvedUserContextText { get; init; }
    public required List<string> FileHashesUsed { get; init; }
    public DateTimeOffset TakenAt { get; init; }
}
