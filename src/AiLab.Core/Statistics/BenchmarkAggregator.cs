using AiLab.Core.Execution;

namespace AiLab.Core.Statistics;

public sealed class BenchmarkResult
{
    public int RunCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public int CanceledCount { get; init; }

    public DescriptiveStatsSummary Duration { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary Ttft { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary GenerationDuration { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary InputTokens { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary OutputTokens { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary TotalTokens { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary Cost { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary OutputTokensPerSecond { get; init; } = DescriptiveStatsSummary.Empty;
}

public sealed class GlobalExecutionSummary
{
    public TimeSpan WallClockDuration { get; init; }

    public TimeSpan CumulativeRequestDuration { get; init; }

    public int TotalInputTokens { get; init; }

    public int TotalCachedInputTokens { get; init; }

    public int TotalUncachedInputTokens { get; init; }

    public int TotalOutputTokens { get; init; }

    public int TotalReasoningTokens { get; init; }

    public int TotalTokens { get; init; }

    public decimal TotalEstimatedCost { get; init; }

    public DescriptiveStatsSummary Duration { get; init; } = DescriptiveStatsSummary.Empty;

    public double AverageTtftMs { get; init; }

    public double AverageGenerationDurationMs { get; init; }

    public double AverageOutputTokensPerSecond { get; init; }
}

/// <summary>Aggregates repeated ExecutionRuns into benchmark/global-summary statistics — ported from the WinForms BenchmarkAggregator.</summary>
public static class BenchmarkAggregator
{
    public static BenchmarkResult ComputeBenchmark(IReadOnlyList<ExecutionRun> runs)
    {
        var completed = runs.Where(r => r.Status == ExecutionStatus.Completed).ToList();

        return new BenchmarkResult
        {
            RunCount = runs.Count,
            SuccessCount = completed.Count,
            FailureCount = runs.Count(r => r.Status == ExecutionStatus.Failed),
            CanceledCount = runs.Count(r => r.Status == ExecutionStatus.Canceled),
            Duration = DescriptiveStatistics.Compute(completed.Where(r => r.TotalDuration.HasValue).Select(r => r.TotalDuration!.Value.TotalMilliseconds).ToArray()),
            Ttft = DescriptiveStatistics.Compute(completed.Where(r => r.TimeToFirstOutputToken.HasValue).Select(r => r.TimeToFirstOutputToken!.Value.TotalMilliseconds).ToArray()),
            GenerationDuration = DescriptiveStatistics.Compute(completed.Where(r => r.GenerationDuration.HasValue).Select(r => r.GenerationDuration!.Value.TotalMilliseconds).ToArray()),
            InputTokens = DescriptiveStatistics.Compute(completed.Select(r => (double)r.Usage.InputTokens).ToArray()),
            OutputTokens = DescriptiveStatistics.Compute(completed.Select(r => (double)r.Usage.OutputTokens).ToArray()),
            TotalTokens = DescriptiveStatistics.Compute(completed.Select(r => (double)r.Usage.TotalTokens).ToArray()),
            Cost = DescriptiveStatistics.Compute(completed.Where(r => r.EstimatedTotalCost.HasValue).Select(r => (double)r.EstimatedTotalCost!.Value).ToArray()),
            OutputTokensPerSecond = DescriptiveStatistics.Compute(completed.Where(r => r.OutputTokensPerSecond.HasValue).Select(r => r.OutputTokensPerSecond!.Value).ToArray()),
        };
    }

    public static GlobalExecutionSummary ComputeGlobalSummary(IReadOnlyList<ExecutionRun> runs, TimeSpan wallClockDuration)
    {
        var completed = runs.Where(r => r.Status == ExecutionStatus.Completed).ToList();
        var cumulative = TimeSpan.FromMilliseconds(completed.Where(r => r.TotalDuration.HasValue).Sum(r => r.TotalDuration!.Value.TotalMilliseconds));

        var ttfts = completed.Where(r => r.TimeToFirstOutputToken.HasValue).Select(r => r.TimeToFirstOutputToken!.Value.TotalMilliseconds).ToArray();
        var genDurations = completed.Where(r => r.GenerationDuration.HasValue).Select(r => r.GenerationDuration!.Value.TotalMilliseconds).ToArray();
        var tokensPerSec = completed.Where(r => r.OutputTokensPerSecond.HasValue).Select(r => r.OutputTokensPerSecond!.Value).ToArray();

        return new GlobalExecutionSummary
        {
            WallClockDuration = wallClockDuration,
            CumulativeRequestDuration = cumulative,
            TotalInputTokens = completed.Sum(r => r.Usage.InputTokens),
            TotalCachedInputTokens = completed.Sum(r => r.Usage.CachedInputTokens),
            TotalUncachedInputTokens = completed.Sum(r => r.Usage.UncachedInputTokens),
            TotalOutputTokens = completed.Sum(r => r.Usage.OutputTokens),
            TotalReasoningTokens = completed.Sum(r => r.Usage.ReasoningTokens),
            TotalTokens = completed.Sum(r => r.Usage.TotalTokens),
            TotalEstimatedCost = completed.Sum(r => r.EstimatedTotalCost ?? 0),
            Duration = DescriptiveStatistics.Compute(completed.Where(r => r.TotalDuration.HasValue).Select(r => r.TotalDuration!.Value.TotalMilliseconds).ToArray()),
            AverageTtftMs = DescriptiveStatistics.Mean(ttfts),
            AverageGenerationDurationMs = DescriptiveStatistics.Mean(genDurations),
            AverageOutputTokensPerSecond = DescriptiveStatistics.Mean(tokensPerSec),
        };
    }
}
