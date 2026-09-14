using AiLab.Core.Execution;

namespace AiLab.Core.Providers;

/// <summary>
/// What a provider hands back to the execution engine after a call completes (or fails).
/// Provider-specific fields that don't fit the common shape live in <see cref="ProviderExtensions"/>
/// rather than being forced onto every provider.
/// </summary>
public sealed class ProviderExecutionResult
{
    public required bool Success { get; init; }

    public string? OutputText { get; init; }

    public string? RawResponseJson { get; init; }

    public string? NormalizedRequestJson { get; init; }

    public string? ResponseId { get; init; }

    public string? ActualModel { get; init; }

    public string? FinishReason { get; init; }

    public int? HttpStatus { get; init; }

    public TokenUsage Usage { get; init; } = new();

    public FailureInfo? Failure { get; init; }

    public IReadOnlyDictionary<string, string> ProviderExtensions { get; init; } = new Dictionary<string, string>();
}
