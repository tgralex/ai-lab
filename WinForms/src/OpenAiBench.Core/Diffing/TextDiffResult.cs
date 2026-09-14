namespace OpenAiBench.Core.Diffing;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed
}

public sealed class DiffLine
{
    public required DiffLineKind Kind { get; init; }
    public required string Text { get; init; }
}

public sealed class TextDiffResult
{
    public required List<DiffLine> Lines { get; init; }
    public bool IsEqual => Lines.All(l => l.Kind == DiffLineKind.Unchanged);
}
