namespace OpenAiBench.Core.Statistics;

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
