namespace OpenAiBench.Infrastructure.Persistence;

public sealed class ExperimentFileDto
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public string Notes { get; init; } = string.Empty;
    public int Order { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public RequestDefinitionDto Request { get; init; } = new();
    public List<AttachedFileDto> Files { get; init; } = new();
    public List<Guid> RunIds { get; init; } = new();
}

public sealed class RequestDefinitionDto
{
    public string Model { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public ContentSetDto CachedContext { get; init; } = new();
    public ContentSetDto UserContext { get; init; } = new();
    public List<VariableBindingDto> Variables { get; init; } = new();
    public string? ResponseSchema { get; init; }
    public string? ReasoningEffort { get; init; }
    public int? MaxOutputTokens { get; init; }
    public bool Stream { get; init; } = true;
    public string? PromptCacheKey { get; init; }
    public Dictionary<string, string> AdditionalSettings { get; init; } = new();
}

public sealed class ContentSetDto
{
    public string Text { get; init; } = string.Empty;
    public List<string> FileIds { get; init; } = new();
}

public sealed class VariableBindingDto
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = "Text";
    public string? TextValue { get; init; }
    public string? FileId { get; init; }
}

public sealed class AttachedFileDto
{
    public string Id { get; init; } = string.Empty;
    public string OriginalFileName { get; init; } = string.Empty;
    public string StoredRelativePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset AddedAt { get; init; }
}

public sealed class RunFileDto
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FirstResponseEventAt { get; init; }
    public DateTimeOffset? FirstTokenAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }

    public double TotalDurationMs { get; init; }
    public double? TimeToFirstResponseEventMs { get; init; }
    public double? TimeToFirstTokenMs { get; init; }
    public double? GenerationDurationMs { get; init; }
    public double? RequestPreparationDurationMs { get; init; }
    public double? FileProcessingDurationMs { get; init; }

    public int InputTokens { get; init; }
    public int CachedInputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public int TotalTokens { get; init; }

    public decimal? EstimatedInputCost { get; init; }
    public decimal? EstimatedCachedInputCost { get; init; }
    public decimal? EstimatedOutputCost { get; init; }
    public decimal? EstimatedCost { get; init; }

    public string Output { get; init; } = string.Empty;
    public string RawResponse { get; init; } = string.Empty;
    public string? RawRequest { get; init; }

    public string? ResponseId { get; init; }
    public string? RequestedModel { get; init; }
    public string? ActualModel { get; init; }

    public string Status { get; init; } = "Idle";

    public string? FinishReason { get; init; }
    public string? Error { get; init; }
    public string? ExceptionType { get; init; }
    public int? HttpStatus { get; init; }
    public string? ApiErrorBody { get; init; }
    public bool FailedAfterStreamingBegan { get; init; }

    public int RetryCount { get; init; }
    public List<double> RetryDelaysMs { get; init; } = new();

    public long RequestByteSize { get; init; }
    public long ResponseByteSize { get; init; }

    public RequestSnapshotDto Snapshot { get; init; } = new();
}

public sealed class RequestSnapshotDto
{
    public RequestDefinitionDto Request { get; init; } = new();
    public string ResolvedSystemPrompt { get; init; } = string.Empty;
    public string ResolvedCachedContextText { get; init; } = string.Empty;
    public string ResolvedUserContextText { get; init; } = string.Empty;
    public List<string> FileHashesUsed { get; init; } = new();
    public DateTimeOffset TakenAt { get; init; }
}

public sealed class WorkspaceStateDto
{
    public int SchemaVersion { get; init; } = 1;
    public List<Guid> ExperimentOrder { get; init; } = new();
    public string? WindowLayoutJson { get; init; }
}
