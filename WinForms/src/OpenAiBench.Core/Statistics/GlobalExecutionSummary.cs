namespace OpenAiBench.Core.Statistics;

public sealed class GlobalExecutionSummary
{
    public TimeSpan WallClockDuration { get; init; }
    public TimeSpan SumOfIndividualDurations { get; init; }

    public int RequestCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int CanceledCount { get; init; }

    public int InputTokens { get; init; }
    public int CachedInputTokens { get; init; }
    public int UncachedInputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public int TotalTokens { get; init; }

    public decimal TotalEstimatedCost { get; init; }

    public DescriptiveStatsSummary LatencyMs { get; init; } = DescriptiveStatsSummary.Empty;

    public double? AverageTimeToFirstTokenMs { get; init; }
    public double? AverageGenerationDurationMs { get; init; }
    public double? AverageOutputTokensPerSecond { get; init; }
}
