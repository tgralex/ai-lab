using AiLab.Core.Statistics;
using Xunit;

namespace AiLab.Tests.Statistics;

public class TextDifferTests
{
    [Fact]
    public void Diff_IdenticalText_AllLinesUnchanged()
    {
        var result = TextDiffer.Diff("a\nb\nc", "a\nb\nc");

        Assert.True(result.IsEqual);
        Assert.All(result.Lines, l => Assert.Equal(DiffLineKind.Unchanged, l.Kind));
    }

    [Fact]
    public void Diff_DetectsAddedAndRemovedLines()
    {
        var result = TextDiffer.Diff("a\nb\nc", "a\nx\nc");

        Assert.False(result.IsEqual);
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "b");
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "x");
    }
}

public class JsonDifferTests
{
    [Fact]
    public void Diff_DetectsChangedValue()
    {
        var result = JsonDiffer.Diff("""{"a": 1}""", """{"a": 2}""");

        Assert.False(result.IsEqual);
        var entry = Assert.Single(result.Differences);
        Assert.Equal(JsonDiffKind.Changed, entry.Kind);
        Assert.Equal("$.a", entry.Path);
    }

    [Fact]
    public void Diff_DetectsAddedAndRemovedProperties()
    {
        var result = JsonDiffer.Diff("""{"a": 1, "b": 2}""", """{"a": 1, "c": 3}""");

        Assert.Contains(result.Differences, d => d.Kind == JsonDiffKind.Removed && d.Path == "$.b");
        Assert.Contains(result.Differences, d => d.Kind == JsonDiffKind.Added && d.Path == "$.c");
    }

    [Fact]
    public void Diff_IdenticalJson_IsEqual()
    {
        var result = JsonDiffer.Diff("""{"a": [1,2,3]}""", """{"a": [1,2,3]}""");

        Assert.True(result.IsEqual);
    }
}
