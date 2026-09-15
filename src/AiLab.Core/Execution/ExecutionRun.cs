namespace AiLab.Core.Execution;

public sealed class ExecutionRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid AiRequestId { get; init; }

    public required string ProviderId { get; init; }

    public string? RequestedModel { get; init; }

    public string? ActualModel { get; set; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Idle;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FirstResponseEventAt { get; set; }

    public DateTimeOffset? FirstOutputTokenAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public TimeSpan? TotalDuration => StartedAt.HasValue && FinishedAt.HasValue ? FinishedAt - StartedAt : null;

    public TimeSpan? TimeToFirstResponseEvent => StartedAt.HasValue && FirstResponseEventAt.HasValue
        ? FirstResponseEventAt - StartedAt
        : null;

    public TimeSpan? TimeToFirstOutputToken => StartedAt.HasValue && FirstOutputTokenAt.HasValue
        ? FirstOutputTokenAt - StartedAt
        : null;

    public TimeSpan? GenerationDuration => FirstOutputTokenAt.HasValue && FinishedAt.HasValue
        ? FinishedAt - FirstOutputTokenAt
        : null;

    public TokenUsage Usage { get; set; } = new();

    public decimal? EstimatedInputCost { get; set; }

    public decimal? EstimatedCachedInputCost { get; set; }

    public decimal? EstimatedOutputCost { get; set; }

    public decimal? EstimatedTotalCost { get; set; }

    public string? Output { get; set; }

    public string? RawProviderResponseJson { get; set; }

    public string? NormalizedProviderRequestJson { get; set; }

    public string? ResponseId { get; set; }

    public string? FinishReason { get; set; }

    public long? RequestSizeBytes { get; set; }

    public long? ResponseSizeBytes { get; set; }

    public FailureInfo? Failure { get; set; }

    public int RetryCount { get; set; }

    public required RequestSnapshot Snapshot { get; init; }

    public bool IsArchived { get; set; }

    public double? OutputTokensPerSecond =>
        GenerationDuration is { } gen && gen.TotalSeconds > 0 && Usage.OutputTokens > 0
            ? Usage.OutputTokens / gen.TotalSeconds
            : null;
}
