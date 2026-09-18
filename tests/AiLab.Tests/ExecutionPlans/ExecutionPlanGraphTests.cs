using AiLab.Core.ExecutionPlans;
using AiLab.Core.Requests;
using Xunit;

namespace AiLab.Tests.ExecutionPlans;

public class ExecutionPlanGraphTests
{
    // Node Id is set equal to the request id, mirroring the pre-existing-data backfill strategy —
    // keeps every test's edges (already expressed in terms of these Guids) working unchanged now
    // that node identity (ExecutionPlanRequest.Id) is distinct from AiRequestId in general.
    private static ExecutionPlan BuildPlan(IReadOnlyList<Guid> requestIds, IReadOnlyList<(Guid From, Guid To)> edges)
    {
        var plan = new ExecutionPlan { WorkspaceId = Guid.NewGuid(), Name = "test-plan" };
        plan.Requests.AddRange(requestIds.Select(id => new ExecutionPlanRequest { Id = id, AiRequestId = id }));
        plan.Dependencies.AddRange(edges.Select(e => new ExecutionPlanDependency { FromNodeId = e.From, ToNodeId = e.To }));
        return plan;
    }

    private static Dictionary<Guid, AiRequestDefinition> BuildRequestsById(IReadOnlyList<Guid> requestIds) =>
        requestIds.ToDictionary(id => id, id => new AiRequestDefinition
        {
            Id = id,
            WorkspaceId = Guid.NewGuid(),
            Name = $"Request-{id}",
            ProviderId = "openai",
            ModelId = "test-model",
            UserContext = new ContentBlock { Text = "hi" },
        });

    [Fact]
    public void Validate_NoDependencies_IsValid()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var plan = BuildPlan([a, b], []);

        var result = ExecutionPlanGraph.Validate(plan, BuildRequestsById([a, b]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SelfLoop_IsRejected()
    {
        var a = Guid.NewGuid();
        var plan = BuildPlan([a], [(a, a)]);

        var result = ExecutionPlanGraph.Validate(plan, BuildRequestsById([a]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot depend on itself"));
    }

    [Fact]
    public void Validate_UnknownRequestInDependency_IsRejected()
    {
        var a = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var plan = BuildPlan([a], [(a, unknown)]);

        var result = ExecutionPlanGraph.Validate(plan, BuildRequestsById([a]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown node"));
    }

    [Fact]
    public void Validate_TwoNodeCycle_IsRejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var plan = BuildPlan([a, b], [(a, b), (b, a)]);

        var result = ExecutionPlanGraph.Validate(plan, BuildRequestsById([a, b]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Cycle detected"));
    }

    [Fact]
    public void Validate_ThreeNodeCycle_IsRejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var plan = BuildPlan([a, b, c], [(a, b), (b, c), (c, a)]);

        var result = ExecutionPlanGraph.Validate(plan, BuildRequestsById([a, b, c]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateEffectiveLabels_IsRejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var plan = BuildPlan([a, b], []);
        var requestsById = BuildRequestsById([a, b]);
        // Give both nodes the same effective label by naming the underlying requests identically.
        requestsById[b].Name = requestsById[a].Name;

        var result = ExecutionPlanGraph.Validate(plan, requestsById);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("labels must be unique"));
    }

    [Fact]
    public void DeriveLevels_DiamondShape_ProducesThreeLevels()
    {
        // A, B independent (level 0); D depends on both A and B (level 1); E depends on D (level 2)
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var d = Guid.NewGuid();
        var e = Guid.NewGuid();
        var plan = BuildPlan([a, b, d, e], [(a, d), (b, d), (d, e)]);

        var levels = ExecutionPlanGraph.DeriveLevels(plan);

        Assert.Equal(3, levels.Count);
        Assert.Equal(2, levels[0].NodeIds.Count);
        Assert.Contains(a, levels[0].NodeIds);
        Assert.Contains(b, levels[0].NodeIds);
        Assert.Single(levels[1].NodeIds);
        Assert.Equal(d, levels[1].NodeIds[0]);
        Assert.Single(levels[2].NodeIds);
        Assert.Equal(e, levels[2].NodeIds[0]);
    }

    [Fact]
    public void DeriveLevels_AllIndependent_SingleLevel()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var plan = BuildPlan(ids, []);

        var levels = ExecutionPlanGraph.DeriveLevels(plan);

        Assert.Single(levels);
        Assert.Equal(3, levels[0].NodeIds.Count);
    }

    [Fact]
    public void DeriveLevels_LinearChain_OneNodePerLevel()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var plan = BuildPlan([a, b, c], [(a, b), (b, c)]);

        var levels = ExecutionPlanGraph.DeriveLevels(plan);

        Assert.Equal(3, levels.Count);
        Assert.All(levels, l => Assert.Single(l.NodeIds));
    }
}
