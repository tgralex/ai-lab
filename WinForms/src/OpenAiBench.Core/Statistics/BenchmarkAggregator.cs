using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Statistics;

public static class BenchmarkAggregator
{
    public static BenchmarkResult ComputeBenchmark(IReadOnlyList<ExecutionRun> runs)
    {
        var successful = runs.Where(r => r.Status == ExecutionStatus.Completed).ToList();

        return new BenchmarkResult
        {
            RunCount = runs.Count,
            SuccessCount = successful.Count,
            FailureCount = runs.Count(r => r.Status == ExecutionStatus.Failed),
            CanceledCount = runs.Count(r => r.Status == ExecutionStatus.Canceled),
            DurationMs = DescriptiveStatistics.Compute(successful.Select(r => r.TotalDuration.TotalMilliseconds).ToArray()),
            TimeToFirstTokenMs = DescriptiveStatistics.Compute(successful.Where(r => r.TimeToFirstToken.HasValue).Select(r => r.TimeToFirstToken!.Value.TotalMilliseconds).ToArray()),
            GenerationDurationMs = DescriptiveStatistics.Compute(successful.Where(r => r.GenerationDuration.HasValue).Select(r => r.GenerationDuration!.Value.TotalMilliseconds).ToArray()),
            InputTokens = DescriptiveStatistics.Compute(successful.Select(r => (double)r.Usage.InputTokens).ToArray()),
            OutputTokens = DescriptiveStatistics.Compute(successful.Select(r => (double)r.Usage.OutputTokens).ToArray()),
            TotalTokens = DescriptiveStatistics.Compute(successful.Select(r => (double)r.Usage.TotalTokens).ToArray()),
            CostUsd = DescriptiveStatistics.Compute(successful.Where(r => r.EstimatedCost.HasValue).Select(r => (double)r.EstimatedCost!.Value).ToArray())
        };
    }

    public static GlobalExecutionSummary ComputeGlobalSummary(IReadOnlyList<ExecutionRun> runs, TimeSpan wallClockDuration)
    {
        var successful = runs.Where(r => r.Status == ExecutionStatus.Completed).ToList();
        var durations = successful.Select(r => r.TotalDuration).ToList();

        return new GlobalExecutionSummary
        {
            WallClockDuration = wallClockDuration,
            SumOfIndividualDurations = durations.Aggregate(TimeSpan.Zero, (acc, d) => acc + d),
            RequestCount = runs.Count,
            SuccessCount = successful.Count,
            FailureCount = runs.Count(r => r.Status == ExecutionStatus.Failed),
            CanceledCount = runs.Count(r => r.Status == ExecutionStatus.Canceled),
            InputTokens = successful.Sum(r => r.Usage.InputTokens),
            CachedInputTokens = successful.Sum(r => r.Usage.CachedInputTokens),
            UncachedInputTokens = successful.Sum(r => r.Usage.UncachedInputTokens),
            OutputTokens = successful.Sum(r => r.Usage.OutputTokens),
            ReasoningTokens = successful.Sum(r => r.Usage.ReasoningTokens),
            TotalTokens = successful.Sum(r => r.Usage.TotalTokens),
            TotalEstimatedCost = successful.Sum(r => r.EstimatedCost ?? 0m),
            LatencyMs = DescriptiveStatistics.Compute(durations.Select(d => d.TotalMilliseconds).ToArray()),
            AverageTimeToFirstTokenMs = Average(successful.Where(r => r.TimeToFirstToken.HasValue).Select(r => r.TimeToFirstToken!.Value.TotalMilliseconds)),
            AverageGenerationDurationMs = Average(successful.Where(r => r.GenerationDuration.HasValue).Select(r => r.GenerationDuration!.Value.TotalMilliseconds)),
            AverageOutputTokensPerSecond = Average(successful.Where(r => r.OutputTokensPerSecond.HasValue).Select(r => r.OutputTokensPerSecond!.Value))
        };
    }

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : list.Average();
    }
}
