namespace OpenAiBench.Core.Diffing;

public enum JsonDiffKind
{
    Added,
    Removed,
    Changed
}

public sealed class JsonDiffEntry
{
    public required string Path { get; init; }
    public required JsonDiffKind Kind { get; init; }
    public string? LeftValue { get; init; }
    public string? RightValue { get; init; }
}

public sealed class JsonDiffResult
{
    public required List<JsonDiffEntry> Differences { get; init; }
    public bool IsEqual => Differences.Count == 0;
}
