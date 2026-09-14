using AiLab.Core.ExecutionPlans;
using Xunit;

namespace AiLab.Tests.ExecutionPlans;

public class ExecutionPlanGraphTests
{
    private static ExecutionPlan BuildPlan(IReadOnlyList<Guid> requestIds, IReadOnlyList<(Guid From, Guid To)> edges)
    {
        var plan = new ExecutionPlan { WorkspaceId = Guid.NewGuid(), Name = "test-plan" };
        plan.Requests.AddRange(requestIds.Select(id => new ExecutionPlanRequest { AiRequestId = id }));
        plan.Dependencies.AddRange(edges.Select(e => new ExecutionPlanDependency { FromRequestId = e.From, ToRequestId = e.To }));
        return plan;
    }

    [Fact]
    public void Validate_NoDependencies_IsValid()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var plan = BuildPlan([a, b], []);

        var result = ExecutionPlanGraph.Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SelfLoop_IsRejected()
    {
        var a = Guid.NewGuid();
        var plan = BuildPlan([a], [(a, a)]);

        var result = ExecutionPlanGraph.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot depend on itself"));
    }

    [Fact]
    public void Validate_UnknownRequestInDependency_IsRejected()
    {
        var a = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var plan = BuildPlan([a], [(a, unknown)]);

        var result = ExecutionPlanGraph.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown request"));
    }

    [Fact]
    public void Validate_TwoNodeCycle_IsRejected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var plan = BuildPlan([a, b], [(a, b), (b, a)]);

        var result = ExecutionPlanGraph.Validate(plan);

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

        var result = ExecutionPlanGraph.Validate(plan);

        Assert.False(result.IsValid);
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
        Assert.Equal(2, levels[0].RequestIds.Count);
        Assert.Contains(a, levels[0].RequestIds);
        Assert.Contains(b, levels[0].RequestIds);
        Assert.Single(levels[1].RequestIds);
        Assert.Equal(d, levels[1].RequestIds[0]);
        Assert.Single(levels[2].RequestIds);
        Assert.Equal(e, levels[2].RequestIds[0]);
    }

    [Fact]
    public void DeriveLevels_AllIndependent_SingleLevel()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var plan = BuildPlan(ids, []);

        var levels = ExecutionPlanGraph.DeriveLevels(plan);

        Assert.Single(levels);
        Assert.Equal(3, levels[0].RequestIds.Count);
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
        Assert.All(levels, l => Assert.Single(l.RequestIds));
    }
}
