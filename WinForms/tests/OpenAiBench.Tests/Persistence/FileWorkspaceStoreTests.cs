using OpenAiBench.Core.Domain;
using OpenAiBench.Infrastructure.Persistence;
using Xunit;

namespace OpenAiBench.Tests.Persistence;

public class FileWorkspaceStoreTests : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), "OpenAiBenchWorkspaceTests_" + Guid.NewGuid());
    private readonly FileWorkspaceStore _store;

    public FileWorkspaceStoreTests()
    {
        _store = new FileWorkspaceStore(_workspacePath);
    }

    [Fact]
    public async Task SaveThenLoadExperiment_RoundTripsAllFields()
    {
        var experiment = new Experiment
        {
            Name = "Round Trip Experiment",
            Tags = { "tag-a", "tag-b" },
            Notes = "some notes",
            Request =
            {
                Model = "gpt-4.1",
                SystemPrompt = "You are a helpful assistant.",
                Stream = false,
                MaxOutputTokens = 512,
                PromptCacheKey = "cache-key-1"
            }
        };
        experiment.Request.CachedContext.Text = "cached text";
        experiment.Request.UserContext.Text = "user text";
        experiment.Request.Variables.Add(new VariableBinding { Name = "resume", Kind = VariableBindingKind.Text, TextValue = "my resume" });

        await _store.SaveExperimentAsync(experiment);
        var loaded = await _store.LoadAllExperimentsAsync();

        var reloaded = Assert.Single(loaded);
        Assert.Equal(experiment.Id, reloaded.Id);
        Assert.Equal("Round Trip Experiment", reloaded.Name);
        Assert.Equal(new[] { "tag-a", "tag-b" }, reloaded.Tags);
        Assert.Equal("some notes", reloaded.Notes);
        Assert.Equal("gpt-4.1", reloaded.Request.Model);
        Assert.False(reloaded.Request.Stream);
        Assert.Equal(512, reloaded.Request.MaxOutputTokens);
        Assert.Equal("cache-key-1", reloaded.Request.PromptCacheKey);
        Assert.Equal("cached text", reloaded.Request.CachedContext.Text);
        Assert.Equal("user text", reloaded.Request.UserContext.Text);
        Assert.Single(reloaded.Request.Variables);
        Assert.Equal("resume", reloaded.Request.Variables[0].Name);
    }

    [Fact]
    public async Task SaveThenLoadRun_RoundTripsTimingTokensAndSnapshot()
    {
        var experiment = new Experiment { Name = "Exp" };
        await _store.SaveExperimentAsync(experiment);

        var run = TestHelpers.CreateRun(
            ExecutionStatus.Completed,
            totalDuration: TimeSpan.FromMilliseconds(1234),
            timeToFirstToken: TimeSpan.FromMilliseconds(200),
            usage: new TokenUsage { InputTokens = 100, CachedInputTokens = 40, OutputTokens = 50, ReasoningTokens = 10, TotalTokens = 150 },
            estimatedCost: 0.0042m);

        await _store.SaveRunAsync(experiment.Id, run);
        var loadedRuns = await _store.LoadRunsAsync(experiment.Id);

        var reloadedRun = Assert.Single(loadedRuns);
        Assert.Equal(run.Id, reloadedRun.Id);
        Assert.Equal(ExecutionStatus.Completed, reloadedRun.Status);
        Assert.Equal(1234, reloadedRun.TotalDuration.TotalMilliseconds, precision: 3);
        Assert.Equal(200, reloadedRun.TimeToFirstToken!.Value.TotalMilliseconds, precision: 3);
        Assert.Equal(100, reloadedRun.Usage.InputTokens);
        Assert.Equal(40, reloadedRun.Usage.CachedInputTokens);
        Assert.Equal(10, reloadedRun.Usage.ReasoningTokens);
        Assert.Equal(0.0042m, reloadedRun.EstimatedCost);
        Assert.Equal("gpt-4.1", reloadedRun.Snapshot.Request.Model);
    }

    [Fact]
    public async Task LoadAllExperiments_PopulatesRunHistoryFromDisk()
    {
        var experiment = new Experiment { Name = "Exp with history" };
        await _store.SaveExperimentAsync(experiment);
        await _store.SaveRunAsync(experiment.Id, TestHelpers.CreateRun());
        await _store.SaveRunAsync(experiment.Id, TestHelpers.CreateRun());

        var loaded = await _store.LoadAllExperimentsAsync();

        Assert.Equal(2, loaded.Single().Runs.Count);
    }

    [Fact]
    public async Task DeleteExperiment_RemovesItFromSubsequentLoads()
    {
        var experiment = new Experiment { Name = "To delete" };
        await _store.SaveExperimentAsync(experiment);

        await _store.DeleteExperimentAsync(experiment.Id);
        var loaded = await _store.LoadAllExperimentsAsync();

        Assert.Empty(loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }
}
