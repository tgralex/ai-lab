namespace AiLab.Core.Requests;

public sealed class AiRequestDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid WorkspaceId { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string ProviderId { get; set; }

    public required string ModelId { get; set; }

    public string? SystemPrompt { get; set; }

    public ContentBlock CachedContext { get; set; } = new();

    public ContentBlock UserContext { get; set; } = new();

    public IReadOnlyList<InputBinding> InputBindings { get; set; } = [];

    public string? StructuredOutputSchema { get; set; }

    public bool StreamingEnabled { get; set; } = true;

    public int? MaxOutputTokens { get; set; }

    public ReasoningConfig? Reasoning { get; set; }

    public IReadOnlyDictionary<string, string> ProviderSettings { get; set; } = new Dictionary<string, string>();

    public string? PromptCacheKey { get; set; }

    public FailurePolicy FailurePolicy { get; set; } = FailurePolicy.FailPlan;

    public RetryPolicy RetryPolicy { get; set; } = new();

    public IReadOnlyList<string> Tags { get; set; } = [];

    public string? Notes { get; set; }

    /// <summary>User-controlled display order within its workspace (lower first) — set by dragging
    /// tasks into place on the workspace page. Ties (e.g. before any manual reordering) break by
    /// <see cref="Name"/>.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A deep copy for one-off "run this same request against a different model" execution —
    /// keeps the SAME Id (so the resulting ExecutionRun still ties back to this saved request's
    /// History) instead of Clone()'s fresh Id, and is never persisted. Since it's built via `new`
    /// rather than fetched via db.Requests, EF never tracks it, so passing it into the executor
    /// carries no risk of the model override leaking back into the saved request on SaveChanges.
    /// </summary>
    public AiRequestDefinition CloneForModel(string providerId, string modelId)
    {
        return new AiRequestDefinition
        {
            Id = Id,
            WorkspaceId = WorkspaceId,
            Name = Name,
            Description = Description,
            ProviderId = providerId,
            ModelId = modelId,
            SystemPrompt = SystemPrompt,
            CachedContext = new ContentBlock { Text = CachedContext.Text, AttachmentIds = [.. CachedContext.AttachmentIds] },
            UserContext = new ContentBlock { Text = UserContext.Text, AttachmentIds = [.. UserContext.AttachmentIds] },
            InputBindings = [.. InputBindings],
            StructuredOutputSchema = StructuredOutputSchema,
            StreamingEnabled = StreamingEnabled,
            MaxOutputTokens = MaxOutputTokens,
            Reasoning = Reasoning is null ? null : new ReasoningConfig { Effort = Reasoning.Effort },
            ProviderSettings = new Dictionary<string, string>(ProviderSettings),
            PromptCacheKey = PromptCacheKey,
            FailurePolicy = FailurePolicy,
            RetryPolicy = new RetryPolicy { MaxRetries = RetryPolicy.MaxRetries, BaseBackoffMs = RetryPolicy.BaseBackoffMs },
            Tags = [.. Tags],
            Notes = Notes,
        };
    }

    public AiRequestDefinition Clone(string? newName = null)
    {
        return new AiRequestDefinition
        {
            WorkspaceId = WorkspaceId,
            Name = newName ?? $"{Name} (copy)",
            Description = Description,
            ProviderId = ProviderId,
            ModelId = ModelId,
            SystemPrompt = SystemPrompt,
            CachedContext = new ContentBlock { Text = CachedContext.Text, AttachmentIds = [.. CachedContext.AttachmentIds] },
            UserContext = new ContentBlock { Text = UserContext.Text, AttachmentIds = [.. UserContext.AttachmentIds] },
            InputBindings = [.. InputBindings],
            StructuredOutputSchema = StructuredOutputSchema,
            StreamingEnabled = StreamingEnabled,
            MaxOutputTokens = MaxOutputTokens,
            Reasoning = Reasoning is null ? null : new ReasoningConfig { Effort = Reasoning.Effort },
            ProviderSettings = new Dictionary<string, string>(ProviderSettings),
            PromptCacheKey = PromptCacheKey,
            FailurePolicy = FailurePolicy,
            RetryPolicy = new RetryPolicy { MaxRetries = RetryPolicy.MaxRetries, BaseBackoffMs = RetryPolicy.BaseBackoffMs },
            Tags = [.. Tags],
            Notes = Notes,
        };
    }
}
