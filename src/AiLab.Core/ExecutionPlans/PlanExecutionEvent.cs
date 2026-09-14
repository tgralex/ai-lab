using AiLab.Core.Execution;

namespace AiLab.Core.ExecutionPlans;

public enum PlanExecutionEventKind
{
    NodeStarted,
    NodeCompleted,
    PlanCompleted,
}

/// <summary>
/// Plan-level progress (node lifecycle), distinct from AiStreamEvent's per-token deltas — a plan
/// run reports when each request starts/finishes, not every intermediate token, to keep multiplexing
/// many concurrent nodes over one SSE connection simple. Single-request execution still streams
/// full token-level deltas via AiRequestExecutor/AiStreamEvent.
/// </summary>
public sealed class PlanExecutionEvent
{
    public required PlanExecutionEventKind Kind { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public Guid? AiRequestId { get; init; }

    public ExecutionRun? Run { get; init; }

    public static PlanExecutionEvent NodeStarted(Guid requestId) => new()
    {
        Kind = PlanExecutionEventKind.NodeStarted,
        Timestamp = DateTimeOffset.UtcNow,
        AiRequestId = requestId,
    };

    public static PlanExecutionEvent NodeCompleted(Guid requestId, ExecutionRun run) => new()
    {
        Kind = PlanExecutionEventKind.NodeCompleted,
        Timestamp = DateTimeOffset.UtcNow,
        AiRequestId = requestId,
        Run = run,
    };

    public static PlanExecutionEvent PlanCompleted() => new()
    {
        Kind = PlanExecutionEventKind.PlanCompleted,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
