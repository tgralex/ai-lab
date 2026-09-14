using AiLab.Core.Execution;
using AiLab.Core.Statistics;
using Xunit;

namespace AiLab.Tests.Statistics;

public class ObservedModelStatisticsTests
{
    private static ExecutionRun BuildRun(double totalMs, double ttftMs, int inputTokens, int outputTokens, decimal cost)
    {
        var started = DateTimeOffset.UtcNow;
        return new ExecutionRun
        {
            AiRequestId = Guid.NewGuid(),
            ProviderId = "openai",
            ActualModel = "gpt-test",
            Status = ExecutionStatus.Completed,
            StartedAt = started,
            FirstOutputTokenAt = started.AddMilliseconds(ttftMs),
            FinishedAt = started.AddMilliseconds(totalMs),
            Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens },
            EstimatedTotalCost = cost,
            Snapshot = new RequestSnapshot { ProviderId = "openai", ModelId = "gpt-test", CapturedAt = started },
        };
    }

    [Fact]
    public void Compute_EmptyRuns_ReturnsZeroSampleCount()
    {
        var result = ObservedModelStatistics.Compute("openai", "gpt-test", []);

        Assert.Equal(0, result.SampleCount);
    }

    [Fact]
    public void Compute_MultipleRuns_AggregatesCorrectly()
    {
        var runs = new[]
        {
            BuildRun(1000, 200, 10, 5, 0.001m),
            BuildRun(2000, 400, 20, 10, 0.002m),
            BuildRun(3000, 600, 30, 15, 0.003m),
        };

        var result = ObservedModelStatistics.Compute("openai", "gpt-test", runs);

        Assert.Equal(3, result.SampleCount);
        Assert.Equal(2000, result.TotalLatency.Median, precision: 3);
        Assert.Equal(400, result.Ttft.Median, precision: 3);
        Assert.Equal(20, result.AverageInputTokens, precision: 3);
        Assert.Equal(10, result.AverageOutputTokens, precision: 3);
        Assert.Equal(0.002, (double)result.AverageCost, precision: 3);
    }

    [Fact]
    public void Compute_ProviderAndModelIdPreserved()
    {
        var result = ObservedModelStatistics.Compute("anthropic", "claude-test", [BuildRun(100, 50, 5, 2, 0.0001m)]);

        Assert.Equal("anthropic", result.ProviderId);
        Assert.Equal("claude-test", result.ModelId);
    }
}
