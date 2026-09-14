using AiLab.Core.Execution;

namespace AiLab.Core.Statistics;

/// <summary>
/// Locally observed performance for one provider/model, computed from actual ExecutionRun history
/// — distinct from (and never conflated with) provider-declared catalog metadata. Not persisted as
/// a denormalized table; computed on demand from ExecutionRun rows so it can never drift from the
/// runs it summarizes.
/// </summary>
public sealed class ObservedModelStatistics
{
    public required string ProviderId { get; init; }

    public required string ModelId { get; init; }

    public int SampleCount { get; init; }

    public DescriptiveStatsSummary Ttft { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary TotalLatency { get; init; } = DescriptiveStatsSummary.Empty;

    public DescriptiveStatsSummary OutputTokensPerSecond { get; init; } = DescriptiveStatsSummary.Empty;

    public double AverageInputTokens { get; init; }

    public double AverageOutputTokens { get; init; }

    public double AverageReasoningTokens { get; init; }

    public decimal AverageCost { get; init; }

    public decimal MedianCost { get; init; }

    public double CacheHitRate { get; init; }

    public static ObservedModelStatistics Compute(string providerId, string modelId, IReadOnlyList<ExecutionRun> completedRuns)
    {
        if (completedRuns.Count == 0)
        {
            return new ObservedModelStatistics { ProviderId = providerId, ModelId = modelId, SampleCount = 0 };
        }

        var ttfts = completedRuns.Where(r => r.TimeToFirstOutputToken.HasValue).Select(r => r.TimeToFirstOutputToken!.Value.TotalMilliseconds).ToArray();
        var durations = completedRuns.Where(r => r.TotalDuration.HasValue).Select(r => r.TotalDuration!.Value.TotalMilliseconds).ToArray();
        var tokensPerSec = completedRuns.Where(r => r.OutputTokensPerSecond.HasValue).Select(r => r.OutputTokensPerSecond!.Value).ToArray();
        var costs = completedRuns.Where(r => r.EstimatedTotalCost.HasValue).Select(r => r.EstimatedTotalCost!.Value).ToArray();

        return new ObservedModelStatistics
        {
            ProviderId = providerId,
            ModelId = modelId,
            SampleCount = completedRuns.Count,
            Ttft = DescriptiveStatistics.Compute(ttfts),
            TotalLatency = DescriptiveStatistics.Compute(durations),
            OutputTokensPerSecond = DescriptiveStatistics.Compute(tokensPerSec),
            AverageInputTokens = completedRuns.Average(r => r.Usage.InputTokens),
            AverageOutputTokens = completedRuns.Average(r => r.Usage.OutputTokens),
            AverageReasoningTokens = completedRuns.Average(r => r.Usage.ReasoningTokens),
            AverageCost = costs.Length > 0 ? costs.Average() : 0,
            MedianCost = costs.Length > 0 ? (decimal)DescriptiveStatistics.Percentile(costs.Select(c => (double)c).ToArray(), 50) : 0,
            CacheHitRate = completedRuns.Average(r => r.Usage.CacheHitPercentage),
        };
    }
}
