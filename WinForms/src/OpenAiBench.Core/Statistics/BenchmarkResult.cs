namespace OpenAiBench.Core.Statistics;

public sealed class BenchmarkResult
{
    public int RunCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int CanceledCount { get; init; }

    public DescriptiveStatsSummary DurationMs { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary TimeToFirstTokenMs { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary GenerationDurationMs { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary InputTokens { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary OutputTokens { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary TotalTokens { get; init; } = DescriptiveStatsSummary.Empty;
    public DescriptiveStatsSummary CostUsd { get; init; } = DescriptiveStatsSummary.Empty;
}
