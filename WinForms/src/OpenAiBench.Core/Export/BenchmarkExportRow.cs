namespace OpenAiBench.Core.Export;

public sealed class BenchmarkExportRow
{
    public required string Experiment { get; init; }
    public required string Run { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string? Model { get; init; }
    public string? Reasoning { get; init; }
    public double TotalMs { get; init; }
    public double? TtftMs { get; init; }
    public double? GenerationMs { get; init; }
    public int InputTokens { get; init; }
    public int CachedTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public double? TokensPerSecond { get; init; }
    public decimal? EstimatedCost { get; init; }
    public required string Status { get; init; }
}
