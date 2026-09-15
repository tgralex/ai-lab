using AiLab.Core.Requests;
using Xunit;

namespace AiLab.Tests.Requests;

public class AiRequestDefinitionTests
{
    private static AiRequestDefinition BuildRequest() => new()
    {
        WorkspaceId = Guid.NewGuid(),
        Name = "Parse Resume",
        ProviderId = "openai",
        ModelId = "gpt-test",
        CachedContext = new ContentBlock { Text = "cached", AttachmentIds = [Guid.NewGuid()] },
        UserContext = new ContentBlock { Text = "user", AttachmentIds = [] },
        Tags = ["a", "b"],
    };

    [Fact]
    public void Clone_GeneratesNewIdAndDefaultSuffixedName()
    {
        var original = BuildRequest();

        var clone = original.Clone();

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal("Parse Resume (copy)", clone.Name);
    }

    [Fact]
    public void Clone_WithExplicitName_UsesThatName()
    {
        var original = BuildRequest();

        var clone = original.Clone("Parse Resume V2");

        Assert.Equal("Parse Resume V2", clone.Name);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = BuildRequest();
        var clone = original.Clone();

        clone.SystemPrompt = "mutated";
        clone.Tags = ["only-on-clone"];

        Assert.Null(original.SystemPrompt);
        Assert.Equal(["a", "b"], original.Tags);
    }

    [Fact]
    public void Clone_PreservesContentAndAttachmentReferences()
    {
        var original = BuildRequest();

        var clone = original.Clone();

        Assert.Equal(original.CachedContext.Text, clone.CachedContext.Text);
        Assert.Equal(original.CachedContext.AttachmentIds, clone.CachedContext.AttachmentIds);
    }

    [Fact]
    public void CloneForModel_PreservesIdSoExecutionRunsTieBackToTheSavedRequest()
    {
        var original = BuildRequest();

        var clone = original.CloneForModel("anthropic", "claude-test");

        Assert.Equal(original.Id, clone.Id);
    }

    [Fact]
    public void CloneForModel_OverridesProviderAndModelOnly()
    {
        var original = BuildRequest();

        var clone = original.CloneForModel("anthropic", "claude-test");

        Assert.Equal("anthropic", clone.ProviderId);
        Assert.Equal("claude-test", clone.ModelId);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.CachedContext.Text, clone.CachedContext.Text);
        Assert.Equal(original.CachedContext.AttachmentIds, clone.CachedContext.AttachmentIds);
    }

    [Fact]
    public void CloneForModel_DoesNotMutateOriginal()
    {
        var original = BuildRequest();

        original.CloneForModel("anthropic", "claude-test");

        Assert.Equal("openai", original.ProviderId);
        Assert.Equal("gpt-test", original.ModelId);
    }
}
