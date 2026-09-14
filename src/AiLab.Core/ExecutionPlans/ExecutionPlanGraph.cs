namespace AiLab.Core.ExecutionPlans;

public sealed class ExecutionPlanValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public required IReadOnlyList<string> Errors { get; init; }

    public static ExecutionPlanValidationResult Ok { get; } = new() { Errors = [] };
}

/// <summary>Derived, ordered set of requests that have no unresolved dependencies among each other — a reporting-level grouping, not the scheduler itself (see ExecutionPlanEngine).</summary>
public sealed class ExecutionLevel
{
    public required int Index { get; init; }

    public required IReadOnlyList<Guid> RequestIds { get; init; }
}

/// <summary>
/// Validates an ExecutionPlan's dependency graph (unknown ids, self-loops, cycles, edges outside the
/// plan) and derives topological levels via Kahn's algorithm. Levels are always derived — never
/// user-specified — and exist for *reporting* (group stats); actual scheduling in
/// AiLab.Core.Execution is dependency-driven, not a strict level-by-level barrier.
/// </summary>
public static class ExecutionPlanGraph
{
    public static ExecutionPlanValidationResult Validate(ExecutionPlan plan)
    {
        var errors = new List<string>();
        var requestIds = plan.Requests.Select(r => r.AiRequestId).ToHashSet();

        foreach (var dependency in plan.Dependencies)
        {
            if (dependency.FromRequestId == dependency.ToRequestId)
            {
                errors.Add($"Request {dependency.FromRequestId} cannot depend on itself.");
                continue;
            }

            if (!requestIds.Contains(dependency.FromRequestId))
            {
                errors.Add($"Dependency references unknown request {dependency.FromRequestId}, which is not in this plan.");
            }

            if (!requestIds.Contains(dependency.ToRequestId))
            {
                errors.Add($"Dependency references unknown request {dependency.ToRequestId}, which is not in this plan.");
            }
        }

        if (errors.Count > 0)
        {
            return new ExecutionPlanValidationResult { Errors = errors };
        }

        var cycle = FindCycle(requestIds, plan.Dependencies);
        if (cycle is not null)
        {
            errors.Add($"Cycle detected among requests: {string.Join(" -> ", cycle)}.");
        }

        return errors.Count == 0 ? ExecutionPlanValidationResult.Ok : new ExecutionPlanValidationResult { Errors = errors };
    }

    /// <summary>DFS-based cycle detection. Returns the offending cycle (as a list of request ids) or null if none.</summary>
    private static List<Guid>? FindCycle(HashSet<Guid> nodes, IReadOnlyList<ExecutionPlanDependency> edges)
    {
        var adjacency = BuildAdjacency(nodes, edges);
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var stack = new List<Guid>();

        foreach (var node in nodes)
        {
            if (visited.Contains(node))
            {
                continue;
            }

            var cycle = Visit(node, adjacency, visiting, visited, stack);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        return null;
    }

    private static List<Guid>? Visit(
        Guid node,
        Dictionary<Guid, List<Guid>> adjacency,
        HashSet<Guid> visiting,
        HashSet<Guid> visited,
        List<Guid> stack)
    {
        visiting.Add(node);
        stack.Add(node);

        foreach (var next in adjacency[node])
        {
            if (visiting.Contains(next))
            {
                var cycleStart = stack.IndexOf(next);
                return [.. stack.Skip(cycleStart), next];
            }

            if (!visited.Contains(next))
            {
                var cycle = Visit(next, adjacency, visiting, visited, stack);
                if (cycle is not null)
                {
                    return cycle;
                }
            }
        }

        visiting.Remove(node);
        stack.RemoveAt(stack.Count - 1);
        visited.Add(node);
        return null;
    }

    /// <summary>Kahn's algorithm: repeatedly peel off nodes with no unresolved incoming edges into successive levels.</summary>
    public static IReadOnlyList<ExecutionLevel> DeriveLevels(ExecutionPlan plan)
    {
        var nodes = plan.Requests.Select(r => r.AiRequestId).ToHashSet();
        var inDegree = nodes.ToDictionary(n => n, _ => 0);
        var adjacency = BuildAdjacency(nodes, plan.Dependencies);

        foreach (var edge in plan.Dependencies)
        {
            inDegree[edge.ToRequestId]++;
        }

        var remaining = new HashSet<Guid>(nodes);
        var levels = new List<ExecutionLevel>();
        var levelIndex = 0;

        while (remaining.Count > 0)
        {
            var current = remaining.Where(n => inDegree[n] == 0).OrderBy(n => n).ToList();
            if (current.Count == 0)
            {
                // Should not happen if Validate() passed first — defensive stop to avoid an infinite loop.
                break;
            }

            levels.Add(new ExecutionLevel { Index = levelIndex++, RequestIds = current });

            foreach (var node in current)
            {
                remaining.Remove(node);
                foreach (var next in adjacency[node])
                {
                    inDegree[next]--;
                }
            }
        }

        return levels;
    }

    private static Dictionary<Guid, List<Guid>> BuildAdjacency(HashSet<Guid> nodes, IReadOnlyList<ExecutionPlanDependency> edges)
    {
        var adjacency = nodes.ToDictionary(n => n, _ => new List<Guid>());
        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.FromRequestId, out var list))
            {
                list.Add(edge.ToRequestId);
            }
        }

        return adjacency;
    }
}
