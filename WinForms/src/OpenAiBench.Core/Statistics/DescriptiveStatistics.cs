namespace OpenAiBench.Core.Statistics;

public static class DescriptiveStatistics
{
    public static DescriptiveStatsSummary Compute(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return DescriptiveStatsSummary.Empty;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mean = sorted.Average();
        var median = Percentile(sorted, 50);

        return new DescriptiveStatsSummary
        {
            Count = sorted.Length,
            Min = sorted[0],
            Max = sorted[^1],
            Mean = mean,
            Median = median,
            P50 = median,
            P90 = Percentile(sorted, 90),
            P95 = Percentile(sorted, 95),
            StdDev = StdDeviation(sorted, mean)
        };
    }

    /// <summary>Linear-interpolation percentile. <paramref name="sortedValues"/> must already be sorted ascending.</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var rank = percentile / 100.0 * (sortedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var weight = rank - lowerIndex;
        return sortedValues[lowerIndex] + weight * (sortedValues[upperIndex] - sortedValues[lowerIndex]);
    }

    public static double StdDeviation(IReadOnlyCollection<double> values, double? precomputedMean = null)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var mean = precomputedMean ?? values.Average();
        var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}
