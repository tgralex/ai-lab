using AiLab.Core.Statistics;
using Xunit;

namespace AiLab.Tests.Statistics;

public class DescriptiveStatisticsTests
{
    [Fact]
    public void Compute_EmptyInput_ReturnsEmptySummary()
    {
        var result = DescriptiveStatistics.Compute([]);

        Assert.Equal(0, result.Count);
        Assert.Equal(0, result.Mean);
        Assert.Equal(0, result.StdDev);
    }

    [Fact]
    public void Compute_SingleValue_AllStatsEqualThatValue()
    {
        var result = DescriptiveStatistics.Compute([42.0]);

        Assert.Equal(1, result.Count);
        Assert.Equal(42, result.Min);
        Assert.Equal(42, result.Max);
        Assert.Equal(42, result.Mean);
        Assert.Equal(42, result.Median);
        Assert.Equal(0, result.StdDev);
    }

    [Fact]
    public void Compute_KnownValues_MatchesExpectedMeanMedianStdDev()
    {
        double[] values = [2, 4, 4, 4, 5, 5, 7, 9];

        var result = DescriptiveStatistics.Compute(values);

        Assert.Equal(5.0, result.Mean, precision: 6);
        Assert.Equal(4.5, result.Median, precision: 6);
        Assert.Equal(2.0, result.StdDev, precision: 6); // population stddev of this well-known example set
    }

    [Theory]
    [InlineData(new double[] { 1, 2, 3, 4, 5 }, 50, 3.0)]
    [InlineData(new double[] { 1, 2, 3, 4, 5 }, 0, 1.0)]
    [InlineData(new double[] { 1, 2, 3, 4, 5 }, 100, 5.0)]
    [InlineData(new double[] { 1, 2, 3, 4, 5 }, 90, 4.6)]
    public void Percentile_LinearInterpolation_MatchesKnownValues(double[] values, double percentile, double expected)
    {
        var result = DescriptiveStatistics.Percentile(values, percentile);

        Assert.Equal(expected, result, precision: 6);
    }

    [Fact]
    public void StdDeviation_ConstantValues_IsZero()
    {
        double[] values = [7, 7, 7, 7];

        var stdDev = DescriptiveStatistics.StdDeviation(values);

        Assert.Equal(0, stdDev);
    }
}
