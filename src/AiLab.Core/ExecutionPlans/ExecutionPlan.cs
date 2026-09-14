namespace AiLab.Core.ExecutionPlans;

public sealed class ExecutionPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid WorkspaceId { get; init; }

    public required string Name { get; set; }

    public List<ExecutionPlanRequest> Requests { get; init; } = [];

    public List<ExecutionPlanDependency> Dependencies { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExecutionPlanRequest
{
    public required Guid AiRequestId { get; init; }

    public bool IsFinalOutput { get; set; }
}

/// <summary>One DAG edge: <see cref="ToRequestId"/> depends on (runs after) <see cref="FromRequestId"/>.</summary>
public sealed class ExecutionPlanDependency
{
    public required Guid FromRequestId { get; init; }

    public required Guid ToRequestId { get; init; }
}
