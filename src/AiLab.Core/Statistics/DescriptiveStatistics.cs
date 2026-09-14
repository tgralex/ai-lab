namespace AiLab.Core.Statistics;

public sealed class DescriptiveStatsSummary
{
    public int Count { get; init; }

    public double Min { get; init; }

    public double Max { get; init; }

    public double Mean { get; init; }

    public double Median { get; init; }

    public double P50 { get; init; }

    public double P90 { get; init; }

    public double P95 { get; init; }

    public double StdDev { get; init; }

    public static DescriptiveStatsSummary Empty { get; } = new();
}

/// <summary>
/// Pure, framework-independent statistics helpers used for both per-request N-run benchmarking
/// and observed model performance. Percentile uses linear interpolation; StdDev is population
/// standard deviation (divides by N) — ported from the WinForms app's DescriptiveStatistics.
/// </summary>
public static class DescriptiveStatistics
{
    public static DescriptiveStatsSummary Compute(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return DescriptiveStatsSummary.Empty;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mean = Mean(sorted);

        return new DescriptiveStatsSummary
        {
            Count = sorted.Length,
            Min = sorted[0],
            Max = sorted[^1],
            Mean = mean,
            Median = Percentile(sorted, 50, alreadySorted: true),
            P50 = Percentile(sorted, 50, alreadySorted: true),
            P90 = Percentile(sorted, 90, alreadySorted: true),
            P95 = Percentile(sorted, 95, alreadySorted: true),
            StdDev = StdDeviation(sorted, mean),
        };
    }

    public static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? 0 : values.Sum() / values.Count;

    public static double Percentile(IReadOnlyList<double> values, double percentile, bool alreadySorted = false)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = alreadySorted ? values : values.OrderBy(v => v).ToArray();
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var rank = percentile / 100.0 * (sorted.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var fraction = rank - lowerIndex;
        return sorted[lowerIndex] + (sorted[upperIndex] - sorted[lowerIndex]) * fraction;
    }

    public static double StdDeviation(IReadOnlyList<double> values, double? precomputedMean = null)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var mean = precomputedMean ?? Mean(values);
        var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}
