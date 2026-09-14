using OpenAiBench.Core.Diffing;
using Xunit;

namespace OpenAiBench.Tests.Diffing;

public class JsonDifferTests
{
    [Fact]
    public void Diff_IdenticalJson_IsEqual()
    {
        var result = JsonDiffer.Diff("""{"a":1,"b":"x"}""", """{"a":1,"b":"x"}""");

        Assert.True(result.IsEqual);
    }

    [Fact]
    public void Diff_ChangedValue_ReportsChangedKind()
    {
        var result = JsonDiffer.Diff("""{"a":1}""", """{"a":2}""");

        var entry = Assert.Single(result.Differences);
        Assert.Equal(JsonDiffKind.Changed, entry.Kind);
        Assert.Equal("$.a", entry.Path);
    }

    [Fact]
    public void Diff_AddedAndRemovedProperties_AreDetected()
    {
        var result = JsonDiffer.Diff("""{"a":1,"removed":true}""", """{"a":1,"added":true}""");

        Assert.Contains(result.Differences, d => d.Kind == JsonDiffKind.Removed && d.Path == "$.removed");
        Assert.Contains(result.Differences, d => d.Kind == JsonDiffKind.Added && d.Path == "$.added");
    }
}
