using OpenAiBench.Core.Domain;
using Xunit;

namespace OpenAiBench.Tests.Domain;

public class ExperimentCloneTests
{
    [Fact]
    public void Clone_AssignsNewIdAndClearsRunHistory()
    {
        var original = new Experiment { Name = "Original" };
        original.Runs.Add(TestHelpers.CreateRun());

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Empty(clone.Runs);
    }

    [Fact]
    public void Clone_DefaultName_AppendsCopySuffix()
    {
        var original = new Experiment { Name = "My Experiment" };

        var clone = original.Clone();

        Assert.Equal("My Experiment (copy)", clone.Name);
    }

    [Fact]
    public void Clone_DeepCopiesRequestSoMutatingCloneDoesNotAffectOriginal()
    {
        var original = new Experiment { Name = "Original" };
        original.Request.SystemPrompt = "original prompt";
        original.Tags.Add("tag1");

        var clone = original.Clone("Clone");
        clone.Request.SystemPrompt = "mutated prompt";
        clone.Tags.Add("tag2");

        Assert.Equal("original prompt", original.Request.SystemPrompt);
        Assert.Single(original.Tags);
    }

    [Fact]
    public void Clone_PreservesFileMetadataReferences()
    {
        var original = new Experiment { Name = "Original" };
        original.Files.Add(new AttachedFileRef
        {
            Id = "file1",
            OriginalFileName = "resume.pdf",
            StoredRelativePath = "files/file1_resume.pdf",
            SizeBytes = 1024,
            ContentType = "application/pdf",
            Sha256 = "abc123",
            AddedAt = DateTimeOffset.UtcNow
        });

        var clone = original.Clone();

        Assert.Single(clone.Files);
        Assert.Equal("resume.pdf", clone.Files[0].OriginalFileName);
    }
}
