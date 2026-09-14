using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Core.Requests;
using AiLab.Core.Statistics;
using AiLab.Tests.TestDoubles;
using Xunit;

namespace AiLab.Tests.Execution;

public class AiRequestExecutorTests
{
    private static AiRequestDefinition BuildRequest(string providerId = "openai") => new()
    {
        WorkspaceId = Guid.NewGuid(),
        Name = "Test Request",
        ProviderId = providerId,
        ModelId = "gpt-test",
        SystemPrompt = "You are a helpful assistant.",
        UserContext = new ContentBlock { Text = "Hello" },
    };

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_PopulatesCompletedRun()
    {
        var fakeProvider = new FakeAiProvider();
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), null, null, CancellationToken.None);

        Assert.Equal(Core.Execution.ExecutionStatus.Completed, run.Status);
        Assert.Equal("fake output", run.Output);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task ExecuteAsync_TracksFirstResponseEventAndFirstOutputTokenSeparately()
    {
        var fakeProvider = new FakeAiProvider { SimulatedDelay = TimeSpan.FromMilliseconds(20) };
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), null, null, CancellationToken.None);

        Assert.NotNull(run.FirstResponseEventAt);
        Assert.NotNull(run.FirstOutputTokenAt);
        // FirstProtocolEvent should fire at/after Started, and OutputTextDelta at/after FirstProtocolEvent.
        Assert.True(run.FirstResponseEventAt >= run.StartedAt);
        Assert.True(run.FirstOutputTokenAt >= run.FirstResponseEventAt);
    }

    [Fact]
    public async Task ExecuteAsync_FirstOutputTokenAt_OnlySetOnFirstDelta()
    {
        var fakeProvider = new FakeAiProvider
        {
            Behavior = async (ctx, progress, ct) =>
            {
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.FirstProtocolEvent));
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: "a"));
                await Task.Delay(5, ct);
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: "b"));
                progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));
                return new ProviderExecutionResult { Success = true, OutputText = "ab" };
            },
        };
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), null, null, CancellationToken.None);

        // Captured on the *first* delta, not overwritten by the second.
        Assert.NotNull(run.FirstOutputTokenAt);
        Assert.True(run.FirstOutputTokenAt <= run.FinishedAt);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderThrowsOperationCanceled_RecordsCanceledNotFailed()
    {
        var fakeProvider = new FakeAiProvider
        {
            Behavior = (_, _, ct) => throw new OperationCanceledException(ct),
        };
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), null, null, CancellationToken.None);

        Assert.Equal(Core.Execution.ExecutionStatus.Canceled, run.Status);
        Assert.Null(run.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderReturnsFailure_RecordsFailedWithFailureInfo()
    {
        var fakeProvider = new FakeAiProvider
        {
            ResultToReturn = new ProviderExecutionResult
            {
                Success = false,
                Failure = new Core.Execution.FailureInfo { Message = "boom", HttpStatus = 500 },
            },
        };
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), null, null, CancellationToken.None);

        Assert.Equal(Core.Execution.ExecutionStatus.Failed, run.Status);
        Assert.Equal("boom", run.Failure?.Message);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProviderId_FailsWithoutCallingAnyProvider()
    {
        var fakeProvider = new FakeAiProvider();
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest("nonexistent"), new BindingResolutionContext(), null, null, CancellationToken.None);

        Assert.Equal(Core.Execution.ExecutionStatus.Failed, run.Status);
        Assert.Equal(0, fakeProvider.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithModelForCost_ComputesEstimatedCost()
    {
        var fakeProvider = new FakeAiProvider
        {
            ResultToReturn = new ProviderExecutionResult
            {
                Success = true,
                OutputText = "x",
                Usage = new Core.Execution.TokenUsage { InputTokens = 1_000_000, OutputTokens = 1_000_000 },
            },
        };
        var model = new ProviderModel { ProviderId = "openai", ModelId = "gpt-test", DisplayName = "GPT Test", InputPricePerMillion = 2m, OutputPricePerMillion = 10m };
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());

        var run = await executor.ExecuteAsync(BuildRequest(), new BindingResolutionContext(), model, null, CancellationToken.None);

        Assert.Equal(12m, run.EstimatedTotalCost);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesWorkspaceVariableBindingsInUserContext()
    {
        var fakeProvider = new FakeAiProvider();
        var executor = new AiRequestExecutor([fakeProvider], new CostCalculator());
        var request = BuildRequest();
        request.UserContext = new ContentBlock { Text = "{{workspace.topic}}" };
        var bindingContext = new BindingResolutionContext { WorkspaceVariables = new Dictionary<string, string> { ["topic"] = "AI Lab" } };

        var run = await executor.ExecuteAsync(request, bindingContext, null, null, CancellationToken.None);

        Assert.Equal("AI Lab", run.Snapshot.ResolvedUserContext);
        Assert.Equal("AI Lab", run.Snapshot.ResolvedBindings["workspace.topic"]);
    }
}
