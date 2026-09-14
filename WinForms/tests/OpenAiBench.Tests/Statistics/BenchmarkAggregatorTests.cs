using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Statistics;
using Xunit;

namespace OpenAiBench.Tests.Statistics;

public class BenchmarkAggregatorTests
{
    [Fact]
    public void ComputeBenchmark_OnlyIncludesCompletedRunsInStats_ButCountsAllStatuses()
    {
        var runs = new[]
        {
            TestHelpers.CreateRun(ExecutionStatus.Completed, TimeSpan.FromMilliseconds(100)),
            TestHelpers.CreateRun(ExecutionStatus.Completed, TimeSpan.FromMilliseconds(200)),
            TestHelpers.CreateRun(ExecutionStatus.Failed),
            TestHelpers.CreateRun(ExecutionStatus.Canceled)
        };

        var result = BenchmarkAggregator.ComputeBenchmark(runs);

        Assert.Equal(4, result.RunCount);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(1, result.CanceledCount);
        Assert.Equal(2, result.DurationMs.Count);
        Assert.Equal(150, result.DurationMs.Mean);
    }

    [Fact]
    public void ComputeGlobalSummary_DistinguishesWallClockFromSummedDurations()
    {
        var runs = new[]
        {
            TestHelpers.CreateRun(ExecutionStatus.Completed, TimeSpan.FromSeconds(3)),
            TestHelpers.CreateRun(ExecutionStatus.Completed, TimeSpan.FromSeconds(4)),
            TestHelpers.CreateRun(ExecutionStatus.Completed, TimeSpan.FromSeconds(5))
        };

        // Simulating parallel execution: wall clock (5s) is much less than the sum of durations (12s).
        var summary = BenchmarkAggregator.ComputeGlobalSummary(runs, TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), summary.WallClockDuration);
        Assert.Equal(TimeSpan.FromSeconds(12), summary.SumOfIndividualDurations);
        Assert.True(summary.WallClockDuration < summary.SumOfIndividualDurations);
    }

    [Fact]
    public void ComputeGlobalSummary_AggregatesTokensAndCostAcrossSuccessfulRunsOnly()
    {
        var runs = new[]
        {
            TestHelpers.CreateRun(ExecutionStatus.Completed, usage: new TokenUsage { InputTokens = 100, OutputTokens = 50 }, estimatedCost: 0.10m),
            TestHelpers.CreateRun(ExecutionStatus.Completed, usage: new TokenUsage { InputTokens = 200, OutputTokens = 75 }, estimatedCost: 0.20m),
            TestHelpers.CreateRun(ExecutionStatus.Failed, usage: new TokenUsage { InputTokens = 999, OutputTokens = 999 }, estimatedCost: 99m)
        };

        var summary = BenchmarkAggregator.ComputeGlobalSummary(runs, TimeSpan.FromSeconds(1));

        Assert.Equal(300, summary.InputTokens);
        Assert.Equal(125, summary.OutputTokens);
        Assert.Equal(0.30m, summary.TotalEstimatedCost);
        Assert.Equal(3, summary.RequestCount);
        Assert.Equal(2, summary.SuccessCount);
        Assert.Equal(1, summary.FailureCount);
    }
}
