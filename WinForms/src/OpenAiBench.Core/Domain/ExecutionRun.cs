namespace OpenAiBench.Core.Domain;

public sealed class ExecutionRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FirstResponseEventAt { get; set; }
    public DateTimeOffset? FirstTokenAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }

    public TimeSpan TotalDuration { get; set; }
    public TimeSpan? TimeToFirstResponseEvent { get; set; }
    public TimeSpan? TimeToFirstToken { get; set; }
    public TimeSpan? GenerationDuration { get; set; }
    public TimeSpan? RequestPreparationDuration { get; set; }
    public TimeSpan? FileProcessingDuration { get; set; }

    public TokenUsage Usage { get; set; } = TokenUsage.Empty;

    public decimal? EstimatedInputCost { get; set; }
    public decimal? EstimatedCachedInputCost { get; set; }
    public decimal? EstimatedOutputCost { get; set; }
    public decimal? EstimatedCost { get; set; }

    public string Output { get; set; } = string.Empty;
    public string RawResponse { get; set; } = string.Empty;
    public string? RawRequest { get; set; }

    public string? ResponseId { get; set; }
    public string? RequestedModel { get; set; }
    public string? ActualModel { get; set; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Idle;

    public string? FinishReason { get; set; }
    public string? Error { get; set; }
    public string? ExceptionType { get; set; }
    public int? HttpStatus { get; set; }
    public string? ApiErrorBody { get; set; }
    public bool FailedAfterStreamingBegan { get; set; }

    public int RetryCount { get; set; }
    public List<TimeSpan> RetryDelays { get; set; } = new();

    public long RequestByteSize { get; set; }
    public long ResponseByteSize { get; set; }

    public bool PromptCacheUsed => Usage.CachedInputTokens > 0;

    public double? OutputTokensPerSecond =>
        GenerationDuration is { } gen && gen.TotalSeconds > 0
            ? Usage.OutputTokens / gen.TotalSeconds
            : null;

    public double? TotalTokensPerSecond =>
        TotalDuration.TotalSeconds > 0
            ? Usage.TotalTokens / TotalDuration.TotalSeconds
            : null;

    public required RequestSnapshot Snapshot { get; init; }
}
