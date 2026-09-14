using OpenAiBench.Core.Diffing;
using Xunit;

namespace OpenAiBench.Tests.Diffing;

public class TextDifferTests
{
    [Fact]
    public void Diff_IdenticalText_AllLinesUnchanged()
    {
        var result = TextDiffer.Diff("line1\nline2", "line1\nline2");

        Assert.True(result.IsEqual);
    }

    [Fact]
    public void Diff_LineChanged_ReportsRemovedAndAdded()
    {
        var result = TextDiffer.Diff("line1\nline2\nline3", "line1\nCHANGED\nline3");

        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "line2");
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "CHANGED");
        Assert.Contains(result.Lines, l => l.Kind == DiffLineKind.Unchanged && l.Text == "line1");
    }
}
