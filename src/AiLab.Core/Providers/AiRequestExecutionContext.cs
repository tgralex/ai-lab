namespace AiLab.Core.Providers;

/// <summary>
/// The fully-resolved request handed to an <see cref="IAiProvider"/> — all input bindings
/// ({{workspace.var}}, {{Request.output}}, {{Request.json.path}}) have already been substituted
/// by <c>AiLab.Core.Execution</c> before the provider ever sees this. Providers stay dumb.
/// </summary>
public sealed class AiRequestExecutionContext
{
    public required string ModelId { get; init; }

    public string? SystemPrompt { get; init; }

    public string? CachedContextText { get; init; }

    public string? UserContextText { get; init; }

    public IReadOnlyList<ExecutionAttachment> Attachments { get; init; } = [];

    public bool Streaming { get; init; } = true;

    public int? MaxOutputTokens { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? StructuredOutputSchema { get; init; }

    public string? PromptCacheKey { get; init; }

    /// <summary>Provider-specific extras that don't fit the common shape (e.g. OpenAI's raw passthrough fields).</summary>
    public IReadOnlyDictionary<string, string> ProviderSettings { get; init; } = new Dictionary<string, string>();
}

public sealed class ExecutionAttachment
{
    public required string Sha256 { get; init; }

    public required string Filename { get; init; }

    public required string MimeType { get; init; }

    public required string AbsolutePath { get; init; }
}
