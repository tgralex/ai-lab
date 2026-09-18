using AiLab.Core.Execution;

namespace AiLab.Core.ExecutionPlans;

public sealed class ExecutionPlanRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ExecutionPlanId { get; init; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Idle;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Wall-clock = actual elapsed time for the whole plan run.</summary>
    public TimeSpan? WallClockDuration => StartedAt.HasValue && FinishedAt.HasValue ? FinishedAt - StartedAt : null;

    /// <summary>Cumulative = sum of every participating run's own duration — shows the parallelism win vs. wall-clock.</summary>
    public TimeSpan CumulativeRequestDuration { get; set; }

    public int TotalInputTokens { get; set; }

    public int TotalCachedInputTokens { get; set; }

    public int TotalOutputTokens { get; set; }

    public int TotalReasoningTokens { get; set; }

    public decimal TotalEstimatedCost { get; set; }

    /// <summary>Snapshot of the plan graph (requests + dependencies) as it was when this run started.</summary>
    public required string PlanGraphSnapshotJson { get; init; }

    public IReadOnlyList<ExecutionGroupRun> Groups { get; set; } = [];

    public IReadOnlyList<Guid> ExecutionRunIds { get; set; } = [];
}

/// <summary>Stats for one derived execution level (a set of requests that ran concurrently).</summary>
public sealed class ExecutionGroupRun
{
    public required int LevelIndex { get; init; }

    public required IReadOnlyList<Guid> NodeIds { get; init; }

    public required IReadOnlyList<Guid> ExecutionRunIds { get; init; }

    public TimeSpan WallClockDuration { get; set; }

    public TimeSpan CumulativeRequestDuration { get; set; }
}
