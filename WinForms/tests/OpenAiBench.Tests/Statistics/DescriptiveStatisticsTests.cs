using OpenAiBench.Core.Statistics;
using Xunit;

namespace OpenAiBench.Tests.Statistics;

public class DescriptiveStatisticsTests
{
    [Fact]
    public void Compute_EmptyInput_ReturnsEmptySummary()
    {
        var result = DescriptiveStatistics.Compute(Array.Empty<double>());

        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.Mean);
    }

    [Fact]
    public void Compute_SingleValue_AllStatsEqualThatValue()
    {
        var result = DescriptiveStatistics.Compute(new[] { 42.0 });

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result.Min);
        Assert.Equal(42, result.Max);
        Assert.Equal(42, result.Mean);
        Assert.Equal(42, result.Median);
        Assert.Equal(0, result.StdDev);
    }

    [Fact]
    public void Compute_KnownDataset_MatchesExpectedMeanMedianStdDev()
    {
        // 2,4,4,4,5,5,7,9 -> mean 5, population stddev 2
        var values = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };

        var result = DescriptiveStatistics.Compute(values);

        Assert.Equal(8, result.Count);
        Assert.Equal(2, result.Min);
        Assert.Equal(9, result.Max);
        Assert.Equal(5, result.Mean, precision: 6);
        Assert.Equal(2, result.StdDev, precision: 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 3)]
    [InlineData(100, 5)]
    public void Percentile_LinearInterpolation_MatchesKnownValues(double percentile, double expected)
    {
        var sorted = new double[] { 1, 2, 3, 4, 5 };

        var result = DescriptiveStatistics.Percentile(sorted, percentile);

        Assert.Equal(expected, result, precision: 6);
    }

    [Fact]
    public void StdDeviation_ConstantValues_IsZero()
    {
        var result = DescriptiveStatistics.StdDeviation(new double[] { 3, 3, 3, 3 });

        Assert.Equal(0, result);
    }
}
