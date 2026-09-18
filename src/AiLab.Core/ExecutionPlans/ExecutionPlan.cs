using AiLab.Core.Requests;

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

/// <summary>
/// One node in a plan's DAG. <see cref="Id"/> is the node's own identity — independent of
/// <see cref="AiRequestId"/> — so the same request can appear as multiple, independently
/// wireable/runnable nodes in one plan.
/// </summary>
public sealed class ExecutionPlanRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid AiRequestId { get; init; }

    /// <summary>Plan-scoped-unique display/binding name. Null falls back to the request's own Name
    /// (see <see cref="EffectiveLabel"/>) — this keeps a single-instance node's {{RequestName.output}}
    /// bindings working unchanged; a second instance of the same request needs a distinct label.</summary>
    public string? Label { get; set; }

    public bool IsFinalOutput { get; set; }

    public string EffectiveLabel(AiRequestDefinition request) =>
        string.IsNullOrWhiteSpace(Label) ? request.Name : Label!;
}

/// <summary>One DAG edge: <see cref="ToNodeId"/> depends on (runs after) <see cref="FromNodeId"/>.
/// Both reference an <see cref="ExecutionPlanRequest.Id"/> (a node), not an AiRequestId.</summary>
public sealed class ExecutionPlanDependency
{
    public required Guid FromNodeId { get; init; }

    public required Guid ToNodeId { get; init; }
}
