using Moq;
using OpenAiBench.Core.Cost;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Ports;
using Xunit;

namespace OpenAiBench.Tests.Execution;

public class ExperimentRunnerTests
{
    private static ExperimentRunner CreateRunner(
        Mock<IOpenAiExperimentClient> client,
        Mock<IWorkspaceStore>? workspaceStore = null,
        Mock<IPricingProvider>? pricingProvider = null)
    {
        workspaceStore ??= new Mock<IWorkspaceStore>();
        pricingProvider ??= new Mock<IPricingProvider>();
        pricingProvider.Setup(p => p.TryGet(It.IsAny<string>())).Returns((PricingEntry?)null);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(() => DateTimeOffset.UtcNow);

        return new ExperimentRunner(
            client.Object,
            workspaceStore.Object,
            Array.Empty<IFileContentExtractor>(),
            clock.Object,
            new CostCalculator(),
            pricingProvider.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SubstitutesTextVariablesIntoResolvedPrompt()
    {
        OpenAiRequestPayload? capturedPayload = null;
        var client = new Mock<IOpenAiExperimentClient>();
        client.Setup(c => c.ExecuteAsync(It.IsAny<OpenAiRequestPayload>(), It.IsAny<IProgress<StreamingUpdate>>(), It.IsAny<CancellationToken>()))
            .Callback<OpenAiRequestPayload, IProgress<StreamingUpdate>?, CancellationToken>((payload, _, _) => capturedPayload = payload)
            .ReturnsAsync((OpenAiRequestPayload payload, IProgress<StreamingUpdate>? _, CancellationToken _) => TestHelpers.CreateRun());

        var experiment = new Experiment { Name = "Exp" };
        experiment.Request.Model = "gpt-4.1";
        experiment.Request.SystemPrompt = "Hello {{name}}, review this: {{job}}";
        experiment.Request.Variables.Add(new VariableBinding { Name = "name", Kind = VariableBindingKind.Text, TextValue = "Alice" });
        experiment.Request.Variables.Add(new VariableBinding { Name = "job", Kind = VariableBindingKind.Text, TextValue = "Senior Engineer" });

        var runner = CreateRunner(client);
        await runner.ExecuteAsync(experiment);

        Assert.NotNull(capturedPayload);
        Assert.Equal("Hello Alice, review this: Senior Engineer", capturedPayload!.ResolvedSystemPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotCapturesRequestAtExecutionTime_UnaffectedByLaterMutation()
    {
        var client = new Mock<IOpenAiExperimentClient>();
        client.Setup(c => c.ExecuteAsync(It.IsAny<OpenAiRequestPayload>(), It.IsAny<IProgress<StreamingUpdate>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OpenAiRequestPayload payload, IProgress<StreamingUpdate>? _, CancellationToken _) => new ExecutionRun { Snapshot = payload.Snapshot, Status = ExecutionStatus.Completed });

        var experiment = new Experiment { Name = "Exp" };
        experiment.Request.Model = "gpt-4.1";
        experiment.Request.SystemPrompt = "original prompt";

        var runner = CreateRunner(client);
        var run = await runner.ExecuteAsync(experiment);

        experiment.Request.SystemPrompt = "mutated after execution";

        Assert.Equal("original prompt", run.Snapshot.Request.SystemPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCanceledToken_RecordsCanceledStatusAndPersistsRun()
    {
        var client = new Mock<IOpenAiExperimentClient>();
        var workspaceStore = new Mock<IWorkspaceStore>();
        var runner = CreateRunner(client, workspaceStore);

        var experiment = new Experiment { Name = "Exp" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var run = await runner.ExecuteAsync(experiment, cancellationToken: cts.Token);

        Assert.Equal(ExecutionStatus.Canceled, run.Status);
        Assert.Contains(run, experiment.Runs);
        workspaceStore.Verify(w => w.SaveRunAsync(experiment.Id, run, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.ExecuteAsync(It.IsAny<OpenAiRequestPayload>(), It.IsAny<IProgress<StreamingUpdate>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ClientThrows_RecordsFailedStatusWithExceptionDetails()
    {
        var client = new Mock<IOpenAiExperimentClient>();
        client.Setup(c => c.ExecuteAsync(It.IsAny<OpenAiRequestPayload>(), It.IsAny<IProgress<StreamingUpdate>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var runner = CreateRunner(client);
        var experiment = new Experiment { Name = "Exp" };

        var run = await runner.ExecuteAsync(experiment);

        Assert.Equal(ExecutionStatus.Failed, run.Status);
        Assert.Equal("boom", run.Error);
        Assert.Equal(nameof(InvalidOperationException), run.ExceptionType);
    }
}
